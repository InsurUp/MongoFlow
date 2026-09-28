using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Semver;

namespace MongoFlow;

internal sealed class VaultMigrator(IServiceProvider services, IEnumerable<IVaultMigrationRunner> runners) : IVaultMigrator
{
    public async Task MigrateAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var runner in runners)
        {
            await runner.MigrateAllAsync(cancellationToken);
        }
    }

    public Task MigrateAsync<TVault>(SemVersion? target = null, CancellationToken cancellationToken = default)
        where TVault : MongoVault =>
        services.GetRequiredService<VaultMigrationRunner<TVault>>().MigrateAsync(target, cancellationToken);

    public Task<SemVersion?> GetVersionAsync<TVault>(CancellationToken cancellationToken = default) where TVault : MongoVault =>
        services.GetRequiredService<VaultMigrationRunner<TVault>>().GetVersionAsync(cancellationToken);
}

internal interface IVaultMigrationRunner
{
    Task MigrateAllAsync(CancellationToken cancellationToken);
}

internal sealed class VaultMigrationRunner<TVault>(IServiceProvider services) : IVaultMigrationRunner where TVault : MongoVault
{
    public async Task MigrateAllAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<TVault>();
        var model = vault.Runtime.Model;

        var existing = await (await model.Database.ListCollectionNamesAsync(cancellationToken: cancellationToken))
            .ToListAsync(cancellationToken);
        var names = existing.ToHashSet();

        foreach (var collection in model.Collections)
        {
            await collection.EnsureCreatedAsync(names, cancellationToken);
        }

        await MigrateAsync(scope.ServiceProvider, vault, target: null, cancellationToken);

        foreach (var collection in model.Collections)
        {
            await collection.EnsureIndexesAsync(cancellationToken);
        }
    }

    public async Task MigrateAsync(SemVersion? target, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var vault = scope.ServiceProvider.GetRequiredService<TVault>();

        await MigrateAsync(scope.ServiceProvider, vault, target, cancellationToken);
    }

    public async Task<SemVersion?> GetVersionAsync(CancellationToken cancellationToken)
    {
        var model = services.GetRequiredService<VaultModelProvider<TVault>>().Model;
        var applied = await AppliedAsync(Records(model), cancellationToken);

        return applied.Max(SemVersion.SortOrderComparer);
    }

    private static async Task MigrateAsync(IServiceProvider scope, TVault vault, SemVersion? target, CancellationToken cancellationToken)
    {
        var model = vault.Runtime.Model;
        if (model.Migrations is null)
        {
            return;
        }

        var migrations = model.Migrations.Types
            .Select(type => (IVaultMigration<TVault>)ActivatorUtilities.CreateInstance(scope, type))
            .OrderBy(migration => migration.Version, SemVersion.SortOrderComparer)
            .ToList();

        var duplicate = migrations.GroupBy(migration => migration.Version).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new VaultConfigurationException(
                $"{typeof(TVault).Name} has more than one migration with version {duplicate.Key}: " +
                $"{string.Join(", ", duplicate.Select(migration => migration.GetType().Name))}.");
        }

        target ??= migrations.LastOrDefault()?.Version;
        if (target is null)
        {
            return;
        }

        var records = Records(model);
        await records.Indexes.CreateOneAsync(
            new CreateIndexModel<MigrationRecord>(Builders<MigrationRecord>.IndexKeys.Ascending(record => record.Version),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        var applied = (await AppliedAsync(records, cancellationToken)).ToHashSet();

        // Every registered migration up to the target that hasn't run, including ones added below the current version.
        foreach (var migration in migrations.Where(migration =>
                     !applied.Contains(migration.Version) && migration.Version.CompareSortOrderTo(target) <= 0))
        {
            await RunAsync(scope, vault, records, migration, up: true, cancellationToken);
        }

        foreach (var migration in Enumerable.Reverse(migrations).Where(migration =>
                     applied.Contains(migration.Version) && migration.Version.CompareSortOrderTo(target) > 0))
        {
            await RunAsync(scope, vault, records, migration, up: false, cancellationToken);
        }
    }

    private static async Task RunAsync(IServiceProvider scope, TVault vault, IMongoCollection<MigrationRecord> records,
        IVaultMigration<TVault> migration, bool up, CancellationToken cancellationToken)
    {
        var model = vault.Runtime.Model;

        try
        {
            if (migration.UseTransaction)
            {
                // Opened in the migration's scope, so the vault's saves join it.
                await using var transaction = scope.GetRequiredService<VaultTransactions>().Start();
                var session = await transaction.JoinAsync(model.Client, cancellationToken);

                await ApplyAsync(session);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                using var session = await model.Client.StartSessionAsync(cancellationToken: cancellationToken);

                await ApplyAsync(session);
            }
        }
        catch (Exception exception) when (exception is not MigrationFailedException)
        {
            throw new MigrationFailedException(typeof(TVault), migration.Version, exception);
        }

        async Task ApplyAsync(IClientSessionHandle session)
        {
            var context = new VaultMigrationContext<TVault>(vault, model.Database, session);

            if (up)
            {
                await migration.UpAsync(context, cancellationToken);
                await records.InsertOneAsync(session, new MigrationRecord
                {
                    Id = ObjectId.GenerateNewId(),
                    Version = migration.Version,
                    Name = migration.GetType().Name,
                    Description = migration.Description,
                    Timestamp = DateTime.UtcNow
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await migration.DownAsync(context, cancellationToken);
                await records.DeleteOneAsync(session, Builders<MigrationRecord>.Filter.Eq(record => record.Version, migration.Version),
                    cancellationToken: cancellationToken);
            }
        }
    }

    private static IMongoCollection<MigrationRecord> Records(VaultModel model) =>
        model.Database.GetCollection<MigrationRecord>(model.Migrations?.CollectionName ?? "migrations");

    private static async Task<List<SemVersion>> AppliedAsync(IMongoCollection<MigrationRecord> records, CancellationToken cancellationToken) =>
        (await records.Find(FilterDefinition<MigrationRecord>.Empty).ToListAsync(cancellationToken))
        .Select(record => record.Version)
        .ToList();
}

internal sealed class VaultMigrationContext<TVault>(TVault vault, IMongoDatabase database, IClientSessionHandle session)
    : MigrationContext<TVault> where TVault : MongoVault
{
    public override TVault Vault => vault;

    public override IMongoDatabase Database => database;

    public override IClientSessionHandle Session => session;
}

/// <summary>An applied migration, in the format earlier MongoFlow versions wrote, so existing history carries over.</summary>
[BsonIgnoreExtraElements]
internal sealed class MigrationRecord
{
    public ObjectId Id { get; init; }

    [BsonSerializer(typeof(SemVersionSerializer))]
    public required SemVersion Version { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required DateTime Timestamp { get; init; }
}

internal sealed class SemVersionSerializer : SerializerBase<SemVersion>
{
    public override SemVersion Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();

        return type == BsonType.String
            ? SemVersion.Parse(context.Reader.ReadString())
            : throw CreateCannotDeserializeFromBsonTypeException(type);
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, SemVersion value) =>
        context.Writer.WriteString(value.ToString());
}

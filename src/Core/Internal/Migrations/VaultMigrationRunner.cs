using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Semver;

namespace MongoFlow;

/// <summary>Applies and reverts <typeparamref name="TVault"/>'s migrations, each in a DI scope of its own.</summary>
internal sealed class VaultMigrationRunner<TVault>(IServiceProvider services) : IVaultMigrationRunner where TVault : MongoVault
{
    private VaultModel Model => services.GetRequiredService<VaultModelProvider<TVault>>().Model;

    public Type VaultType => typeof(TVault);

    public IMongoCollection<MigrationRecord>? History =>
        Model is { Migrations: { } migrations } model
            ? model.Database.GetCollection<MigrationRecord>(migrations.CollectionName)
            : null;

    public async Task MigrateAsync(SemVersion? target,
        CancellationToken cancellationToken)
    {
        if (History is not { } history)
        {
            return;
        }

        using var activity = VaultActivities.StartMigrate(typeof(TVault).Name);
        try
        {
            await MigrateAsync(history, target, activity, cancellationToken);
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }

    public async Task<SemVersion?> GetVersionAsync(CancellationToken cancellationToken) =>
        History is { } history ? (await AppliedAsync(history, cancellationToken)).Max(SemVersion.SortOrderComparer) : null;

    private async Task MigrateAsync(IMongoCollection<MigrationRecord> history,
        SemVersion? target,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var migrations = await PlanAsync();
        target ??= Pinned(migrations) ?? migrations[^1].Version;

        // Two instances recording one version at once: the second fails, rolling back its migration if it has a transaction.
        await history.Indexes.CreateOneAsync(
            new CreateIndexModel<MigrationRecord>(Builders<MigrationRecord>.IndexKeys.Ascending(record => record.Version),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: cancellationToken);

        var applied = await AppliedAsync(history, cancellationToken);

        // Every migration up to the target that hasn't been applied, including ones added below the current version.
        var applying = migrations
            .Where(migration => !applied.Contains(migration.Version) && migration.Version.CompareSortOrderTo(target) <= 0)
            .ToList();
        var reverting = Enumerable.Reverse(migrations)
            .Where(migration => applied.Contains(migration.Version) && migration.Version.CompareSortOrderTo(target) > 0)
            .ToList();

        var log = Model.Logs.Migrations;
        var current = applied.Max(SemVersion.SortOrderComparer)?.ToString() ?? "none";
        activity.Migrating(current, target);

        if (applying.Count == 0 && reverting.Count == 0)
        {
            log.UpToDate(typeof(TVault).Name, current);
            return;
        }

        log.Migrating(typeof(TVault).Name, current, target, applying.Count, reverting.Count);

        foreach (var migration in applying)
        {
            await RunAsync(history, migration, up: true, cancellationToken);
        }

        foreach (var migration in reverting)
        {
            await RunAsync(history, migration, up: false, cancellationToken);
        }
    }

    /// <summary>The migrations' types and versions, oldest first. Reading a version takes an instance, from a scope of its own.</summary>
    private async Task<List<(Type Type, SemVersion Version)>> PlanAsync()
    {
        await using var scope = services.CreateAsyncScope();

        var migrations = Model.Migrations!.Types
            .Select(type => (Type: type, Create(scope.ServiceProvider, type).Version))
            .OrderBy(migration => migration.Version, SemVersion.SortOrderComparer)
            .ToList();

        var duplicate = migrations.GroupBy(migration => migration.Version).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new VaultConfigurationException(
                $"{typeof(TVault).Name} has more than one migration with version {duplicate.Key}: " +
                $"{string.Join(", ", duplicate.Select(migration => migration.Type.Name))}.");
        }

        return migrations;
    }

    /// <summary>The version the vault's <see cref="MongoVersionAttribute"/> names, which must be one of its migrations'.</summary>
    private SemVersion? Pinned(List<(Type Type, SemVersion Version)> migrations)
    {
        if (Model.Migrations!.Target is not { } pinned)
        {
            return null;
        }

        return migrations.Exists(migration => migration.Version == pinned)
            ? pinned
            : throw new VaultConfigurationException(
                $"{typeof(TVault).Name} is at version {pinned} by its [MongoVersion], but none of its migrations has that " +
                $"version. Its versions: {string.Join(", ", migrations.Select(migration => migration.Version))}.");
    }

    private async Task RunAsync(IMongoCollection<MigrationRecord> history,
        (Type Type, SemVersion Version) planned,
        bool up,
        CancellationToken cancellationToken)
    {
        // Started first, so the migration's scope, transaction and writes are traced under it.
        using var activity = VaultActivities.StartMigration(typeof(TVault).Name, planned.Version, planned.Type.Name, up);
        await using var scope = services.CreateAsyncScope();
        var migration = Create(scope.ServiceProvider, planned.Type);
        var vault = scope.ServiceProvider.GetRequiredService<TVault>();
        var model = Model;
        var started = Stopwatch.GetTimestamp();

        try
        {
            if (migration.UseTransaction)
            {
                // Opened in the migration's scope, so the vault's saves join it.
                await using var transaction = scope.ServiceProvider.GetRequiredService<VaultTransactionManager>().Start(forSave: false);
                var session = await transaction.GetSessionAsync(model.Client, cancellationToken);

                await ApplyAsync(session);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                using var session = await model.Client.StartSessionAsync(cancellationToken: cancellationToken);

                await ApplyAsync(session);
            }
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            model.Logs.Migrations.MigrationFailed(up ? "Applying" : "Reverting", planned.Version, typeof(TVault).Name,
                planned.Type.Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception);

            throw new MigrationFailedException(typeof(TVault), planned.Version, up, exception);
        }

        model.Logs.Migrations.MigrationApplied(up ? "Applied" : "Reverted", planned.Version, typeof(TVault).Name,
            planned.Type.Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        async Task ApplyAsync(IClientSessionHandle session)
        {
            var context = new MigrationContext<TVault>(vault, model.Database, session);

            if (up)
            {
                await migration.UpAsync(context, cancellationToken);
                ThrowIfUnsaved();

                await history.InsertOneAsync(session, new MigrationRecord
                {
                    Id = ObjectId.GenerateNewId(),
                    Version = planned.Version,
                    Name = planned.Type.Name,
                    Description = migration.Description,
                    Timestamp = (scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await migration.DownAsync(context, cancellationToken);
                ThrowIfUnsaved();

                await history.DeleteOneAsync(session, Builders<MigrationRecord>.Filter.Eq(record => record.Version, planned.Version),
                    cancellationToken: cancellationToken);
            }
        }

        // The vault is the migration's alone, so writes still queued were meant to be saved and never will be.
        void ThrowIfUnsaved()
        {
            if (vault.Runtime.Drain() is { } unsaved)
            {
                unsaved.Dispose();

                throw new InvalidOperationException(
                    $"{planned.Type.Name} queued writes on {typeof(TVault).Name} without saving them. Call SaveAsync before " +
                    "the migration returns.");
            }
        }
    }

    private static IVaultMigration<TVault> Create(IServiceProvider scope,
        Type type) =>
        (IVaultMigration<TVault>)ActivatorUtilities.CreateInstance(scope, type);

    private static async Task<HashSet<SemVersion>> AppliedAsync(IMongoCollection<MigrationRecord> history,
        CancellationToken cancellationToken) =>
        (await history.Find(FilterDefinition<MigrationRecord>.Empty).ToListAsync(cancellationToken))
        .Select(record => record.Version)
        .ToHashSet();
}

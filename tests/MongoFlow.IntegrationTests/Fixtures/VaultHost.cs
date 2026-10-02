using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A <typeparamref name="TVault"/> registered on a service provider of its own, on <paramref name="database"/>, with
/// <paramref name="client"/> as the registered <see cref="IMongoClient"/>, and resolved from a scope.
/// </summary>
public sealed class VaultHost<TVault>(IMongoClient client,
    IMongoDatabase database,
    Action<IVaultBuilder<TVault>>? configure = null,
    Action<IServiceCollection>? services = null) : IAsyncDisposable where TVault : MongoVault
{
    private readonly ServiceProvider _provider = Build(client, database, configure, services);
    private AsyncServiceScope? _scope;

    public IMongoDatabase Database { get; } = database;

    public IServiceProvider Services => (_scope ??= _provider.CreateAsyncScope()).ServiceProvider;

    /// <summary>The scope's vault; within the scope it's the same instance.</summary>
    public TVault Vault => Services.GetRequiredService<TVault>();

    public IVaultTransactionManager Transactions => Services.GetRequiredService<IVaultTransactionManager>();

    /// <summary>Another scope, as another request would have, with its own vault and transaction.</summary>
    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public Task SeedAsync<TDocument>(string collection,
        params TDocument[] documents) =>
        Database.GetCollection<TDocument>(collection).InsertManyAsync(documents);

    /// <summary>What <paramref name="collection"/> holds, as stored, in <c>_id</c> order.</summary>
    public Task<List<BsonDocument>> StoredAsync(string collection) =>
        Database.GetCollection<BsonDocument>(collection)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync();

    public async ValueTask DisposeAsync()
    {
        if (_scope is { } scope)
        {
            await scope.DisposeAsync();
        }

        await _provider.DisposeAsync();
    }

    private static ServiceProvider Build(IMongoClient client,
        IMongoDatabase database,
        Action<IVaultBuilder<TVault>>? configure,
        Action<IServiceCollection>? services)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(client);
        services?.Invoke(collection);
        collection.AddMongoVault<TVault>(vault =>
        {
            vault.UseDatabase(database);
            configure?.Invoke(vault);
        });

        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

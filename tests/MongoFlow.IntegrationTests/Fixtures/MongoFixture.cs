using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using TUnit.Core.Interfaces;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A throwaway MongoDB shared by every test in the run (<c>[ClassDataSource&lt;MongoFixture&gt;(Shared =
/// SharedType.PerTestSession)]</c>): a single-node replica set, since saves run in transactions. Tests stay isolated by
/// each using a database of its own, so one server is safe to share.
/// </summary>
public sealed class MongoFixture : IAsyncInitializer, IAsyncDisposable
{
    // 8.0 is the first server with client bulk writes, which every save uses.
    private const string Image = "mongo:8.2";

    private readonly MongoDbContainer _container = new MongoDbBuilder(Image)
        .WithReplicaSet()
        // Failpoints, to make the server fail a command on cue.
        .WithCommand("--setParameter", "enableTestCommands=1")
        .Build();

    private MongoClient? _client;

    /// <summary>The client every test shares, unless it needs one of its own.</summary>
    public IMongoClient Client => _client ?? throw new InvalidOperationException("The fixture hasn't started.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _client = new MongoClient(_container.GetConnectionString());
    }

    /// <summary>A database no other test uses.</summary>
    public IMongoDatabase NewDatabase() => Client.GetDatabase($"t{Guid.NewGuid():N}");

    /// <summary>A client of its own, named so a failpoint can single it out.</summary>
    public MongoClient CreateClient(string applicationName)
    {
        var settings = MongoClientSettings.FromConnectionString(_container.GetConnectionString());
        settings.ApplicationName = applicationName;

        return new MongoClient(settings);
    }

    /// <summary>Makes the server fail the next <paramref name="command"/> from clients named <paramref name="applicationName"/>.</summary>
    public Task FailNextAsync(string command,
        string applicationName) =>
        Client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "configureFailPoint", "failCommand" },
            { "mode", new BsonDocument("times", 1) },
            {
                "data", new BsonDocument
                {
                    { "failCommands", new BsonArray { command } },
                    // BadValue: not retryable, so the driver reports it instead of trying again.
                    { "errorCode", 2 },
                    { "appName", applicationName }
                }
            }
        });

    /// <summary>A <typeparamref name="TVault"/> on a new database of the shared client.</summary>
    public VaultHost<TVault> Host<TVault>(Action<IVaultBuilder<TVault>>? configure = null,
        Action<IServiceCollection>? services = null) where TVault : MongoVault =>
        new(Client, NewDatabase(), configure, services);

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _container.DisposeAsync();
    }
}

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using TUnit.Core.Interfaces;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A throwaway MongoDB with MongoDB Search, shared by every search test in the run (<c>[ClassDataSource&lt;SearchFixture&gt;(
/// Shared = SharedType.PerTestSession)]</c>): MongoDB's Atlas Local image, a single-node replica set of the community server
/// with its search process, mongot. Tests stay isolated by each using a database of its own.
/// </summary>
public sealed class SearchFixture : IAsyncInitializer, IAsyncDisposable
{
    // mongod 8.3 and mongot 1.70, as InsurUp runs MongoDB Search on Community.
    private const string Image = "mongodb/mongodb-atlas-local:8.3.4";

    private const ushort Port = 27017;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(Port, assignRandomHostPort: true)
        // The image's health check, run until it passes, rather than once every 30 seconds as Docker runs it: it checks
        // both mongod and mongot.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("/usr/local/bin/runner", "healthcheck"))
        .Build();

    private MongoClient? _client;

    /// <summary>The client every test shares.</summary>
    public IMongoClient Client => _client ?? throw new InvalidOperationException("The fixture hasn't started.");

    private string ConnectionString => $"mongodb://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/?directConnection=true";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _client = new MongoClient(ConnectionString);
    }

    /// <summary>A database no other test uses.</summary>
    public IMongoDatabase NewDatabase() => Client.GetDatabase($"t{Guid.NewGuid():N}");

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

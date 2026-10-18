using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using MongoDB.Driver;
using TUnit.Core.Interfaces;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A throwaway sharded cluster, shared by every test that needs one (<c>[ClassDataSource&lt;ShardedClusterFixture&gt;(Shared
/// = SharedType.PerTestSession)]</c>): a config server that's a shard too, as MongoDB 8.0 lets it be, and mongos, on a
/// network of their own. Tests stay isolated by each using a database of its own.
/// </summary>
public sealed class ShardedClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string Image = "mongo:8.2";

    private const ushort Port = 27017;

    private const ushort ConfigPort = 27019;

    // The config server's replica set, as mongos reaches it, once it has elected its primary.
    private const string Setup = """
        rs.initiate({ _id: 'cfg', configsvr: true, members: [{ _id: 0, host: 'cfg:27019' }] });
        for (let i = 0; !db.hello().isWritablePrimary; i++) { if (i === 600) throw new Error('No primary'); sleep(100); }
        """;

    private readonly INetwork _network;
    private readonly IContainer _config;
    private readonly IContainer _mongos;
    private MongoClient? _client;

    public ShardedClusterFixture()
    {
        _network = new NetworkBuilder().Build();
        _config = new ContainerBuilder(Image)
            .WithNetwork(_network)
            .WithNetworkAliases("cfg")
            .WithCommand("--configsvr", "--replSet", "cfg", "--bind_ip_all", "--port", $"{ConfigPort}")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilCommandIsCompleted("mongosh", "--port", $"{ConfigPort}", "--quiet", "--eval", "db.runCommand({ ping: 1 })"))
            .Build();
        _mongos = new ContainerBuilder(Image)
            .WithNetwork(_network)
            .WithNetworkAliases("mongos")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithCommand("mongos", "--configdb", $"cfg/cfg:{ConfigPort}", "--bind_ip_all", "--port", $"{Port}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("mongosh", "--quiet", "--eval", "db.runCommand({ ping: 1 })"))
            .Build();
    }

    /// <summary>The client every test shares, of mongos.</summary>
    public IMongoClient Client => _client ?? throw new InvalidOperationException("The fixture hasn't started.");

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        await _config.StartAsync();
        await RunAsync(_config, ["mongosh", "--port", $"{ConfigPort}", "--quiet", "--eval", Setup]);
        await _mongos.StartAsync();

        // The config server holds the cluster's data too, so the cluster needs no shard of its own.
        await RunAsync(_mongos, ["mongosh", "--quiet", "--eval", "db.adminCommand({ transitionFromDedicatedConfigServer: 1 })"]);
        _client = new MongoClient($"mongodb://{_mongos.Hostname}:{_mongos.GetMappedPublicPort(Port)}");
    }

    /// <summary>A database no other test uses.</summary>
    public IMongoDatabase NewDatabase() => Client.GetDatabase($"t{Guid.NewGuid():N}");

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _mongos.DisposeAsync();
        await _config.DisposeAsync();
        await _network.DisposeAsync();
    }

    // Runs command in container, failing with what it wrote if it fails.
    private static async Task RunAsync(IContainer container,
        IList<string> command)
    {
        var result = await container.ExecAsync(command);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{command[0]} failed: {result.Stderr}{result.Stdout}");
        }
    }
}

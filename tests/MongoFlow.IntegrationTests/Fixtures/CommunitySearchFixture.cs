using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// MongoDB Community with MongoDB Search as InsurUp runs it: mongod, and mongot from the mongodb-community-search image,
/// on a network of their own. mongot syncs from mongod as a user with the searchCoordinator role, and lists an index's
/// definition as it was created, without the defaults it fills in, nor its type.
/// </summary>
public sealed class CommunitySearchFixture : SearchFixture
{
    private const string MongodImage = "mongo:8.2";

    private const string MongotImage = "mongodb/mongodb-community-search:1.70.1";

    private const ushort Port = 27017;

    private const string Password = "mongot";

    // mongot syncs from mongod, and serves mongod's search commands over gRPC, both without TLS.
    private const string Config = """
        syncSource:
          replicaSet:
            hostAndPort: "mongod:27017"
            scramAuth:
              username: "mongotUser"
              authSource: "admin"
              passwordFile: "/mongot-community/pwfile"
              tls:
                enabled: false
        storage:
          dataPath: "/data/mongot"
        server:
          grpc:
            address: "0.0.0.0:27028"
            tls:
              mode: "disabled"
        metrics:
          enabled: false
        healthCheck:
          address: "0.0.0.0:8080"
        logging:
          verbosity: WARN
        """;

    // The replica set, named as mongot reaches it; and mongot's user, once the replica set has elected its primary.
    private const string Setup = $$"""
        rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: 'mongod:27017' }] });
        for (let i = 0; !db.hello().isWritablePrimary; i++) { if (i === 600) throw new Error('No primary'); sleep(100); }
        db.getSiblingDB('admin').createUser({ user: 'mongotUser', pwd: '{{Password}}', roles: ['searchCoordinator'] });
        """;

    // mongot serves search commands a moment after it starts, once it has synced the topology: until then, creating a
    // search index fails, so one is created until it doesn't.
    private const string Probe = """
        const probe = db.getSiblingDB('probe');
        probe.createCollection('probe');
        for (let i = 0; ; i++) {
          try { probe.probe.createSearchIndex('probe', { mappings: { dynamic: true } }); break; }
          catch (e) { if (i === 600) throw e; sleep(100); }
        }
        probe.dropDatabase();
        """;

    private readonly INetwork _network;
    private readonly IContainer _mongod;
    private readonly IContainer _mongot;

    public CommunitySearchFixture()
    {
        _network = new NetworkBuilder().Build();
        _mongod = new ContainerBuilder(MongodImage)
            .WithNetwork(_network)
            .WithNetworkAliases("mongod")
            .WithPortBinding(Port, assignRandomHostPort: true)
            .WithCommand("--replSet", "rs0", "--bind_ip_all",
                "--setParameter", "mongotHost=mongot:27028",
                "--setParameter", "searchIndexManagementHostAndPort=mongot:27028",
                "--setParameter", "useGrpcForSearch=true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("mongosh", "--quiet", "--eval", "db.runCommand({ ping: 1 })"))
            .Build();
        _mongot = new ContainerBuilder(MongotImage)
            .WithNetwork(_network)
            .WithNetworkAliases("mongot")
            .WithResourceMapping(Encoding.UTF8.GetBytes(Config), "/mongot-community/config.default.yml")
            // mongot refuses a password file anyone but its owner can read.
            .WithResourceMapping(Encoding.UTF8.GetBytes(Password), "/mongot-community/pwfile", fileMode: UnixFileModes.UserRead)
            .Build();
    }

    // How snapshots received on it are named.
    public override string ToString() => "Community";

    protected override async Task<string> StartAsync()
    {
        await _network.CreateAsync();
        await _mongod.StartAsync();
        await RunAsync(Setup);
        await _mongot.StartAsync();
        await RunAsync(Probe);
        return $"mongodb://{_mongod.Hostname}:{_mongod.GetMappedPublicPort(Port)}/?directConnection=true";
    }

    protected override async ValueTask StopAsync()
    {
        await _mongot.DisposeAsync();
        await _mongod.DisposeAsync();
        await _network.DisposeAsync();
    }

    // Runs script in mongod's shell, failing with what it wrote if it fails.
    private async Task RunAsync(string script)
    {
        var result = await _mongod.ExecAsync(["mongosh", "--quiet", "--eval", script]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"mongosh failed: {result.Stderr}{result.Stdout}");
        }
    }
}

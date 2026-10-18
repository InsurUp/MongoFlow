using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// MongoDB's Atlas Local image, a single-node replica set of the community server with its search process, mongot: it lists
/// an index's definition with the defaults it fills in, and its type.
/// </summary>
public sealed class AtlasLocalFixture : SearchFixture
{
    // mongod 8.3 and mongot 1.70.
    private const string Image = "mongodb/mongodb-atlas-local:8.3.4";

    private const ushort Port = 27017;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(Port, assignRandomHostPort: true)
        // The image's health check, run until it passes, rather than once every 30 seconds as Docker runs it: it checks
        // both mongod and mongot.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("/usr/local/bin/runner", "healthcheck"))
        .Build();

    // How snapshots received on it are named.
    public override string ToString() => "AtlasLocal";

    protected override async Task<string> StartAsync()
    {
        await _container.StartAsync();
        return $"mongodb://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}/?directConnection=true";
    }

    protected override ValueTask StopAsync() => _container.DisposeAsync();
}

using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>
/// The MongoDB the samples run against: the one <c>ConnectionStrings:Mongo</c> names, or MongoDB's Atlas Local image
/// started in Docker for the run, a replica set with MongoDB Search. Saves need MongoDB 8.0 or later, as a replica set.
/// </summary>
public static class SampleServer
{
    private const ushort Port = 27017;

    public static async Task<IAsyncDisposable?> StartAsync(IConfigurationManager configuration)
    {
        if (configuration.GetConnectionString("Mongo") is not null)
        {
            return null;
        }

        var container = new ContainerBuilder("mongodb/mongodb-atlas-local:8.3.4")
            .WithPortBinding(Port, assignRandomHostPort: true)
            // Its health check covers mongod and mongot, the search process.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("/usr/local/bin/runner", "healthcheck"))
            .WithLogger(NullLogger.Instance)
            .Build();
        await container.StartAsync();
        configuration["ConnectionStrings:Mongo"] = $"mongodb://{container.Hostname}:{container.GetMappedPublicPort(Port)}/?directConnection=true";

        return container;
    }
}

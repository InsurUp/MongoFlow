using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MongoDb;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>
/// The MongoDB the samples run against: the one <c>ConnectionStrings:Mongo</c> names, or a replica set started in
/// Docker for the run. Saves need MongoDB 8.0 or later, as a replica set.
/// </summary>
public static class SampleServer
{
    public static async Task<IAsyncDisposable?> StartAsync(IConfigurationManager configuration)
    {
        if (configuration.GetConnectionString("Mongo") is not null)
        {
            return null;
        }

        var container = new MongoDbBuilder("mongo:8.2").WithReplicaSet().WithLogger(NullLogger.Instance).Build();
        await container.StartAsync();
        configuration["ConnectionStrings:Mongo"] = container.GetConnectionString();

        return container;
    }
}

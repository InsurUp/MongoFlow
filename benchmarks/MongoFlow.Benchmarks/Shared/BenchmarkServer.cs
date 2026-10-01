using Testcontainers.MongoDb;

namespace MongoFlow.Benchmarks;

/// <summary>
/// The MongoDB the benchmarks that save write to. BenchmarkDotNet runs each benchmark in a process of its own, which
/// inherits the environment, so the server is named in <see cref="Variable"/>: by whoever runs the benchmarks, or by
/// <see cref="StartAsync"/> after starting a container for the run.
/// </summary>
public static class BenchmarkServer
{
    public const string Variable = "MONGOFLOW_BENCHMARKS_MONGO";

    // 8.0 is the first server with client bulk writes, which every save uses.
    private const string Image = "mongo:8.2";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(Variable)
        ?? throw new InvalidOperationException($"No MongoDB to benchmark against: set {Variable}, or run through Program.");

    /// <summary>
    /// Starts a replica set for the run and names it in <see cref="Variable"/>, unless a server is named already or the
    /// run only lists benchmarks.
    /// </summary>
    public static async Task<IAsyncDisposable?> StartAsync(string[] args)
    {
        if (Environment.GetEnvironmentVariable(Variable) is not null ||
            args.Any(arg => arg is "--list" or "--help" or "-h" or "--info" or "--version"))
        {
            return null;
        }

        var container = new MongoDbBuilder(Image).WithReplicaSet().Build();
        await container.StartAsync();
        Environment.SetEnvironmentVariable(Variable, container.GetConnectionString());

        return container;
    }
}

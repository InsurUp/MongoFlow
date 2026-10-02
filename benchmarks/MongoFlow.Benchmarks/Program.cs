using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using MongoFlow.Benchmarks;

// From the repository root:
//   dotnet run -c Release --project benchmarks/MongoFlow.Benchmarks -- --filter '*'              every benchmark
//   dotnet run -c Release --project benchmarks/MongoFlow.Benchmarks -- --filter '*Insert*' --job short
// The benchmarks that save need MongoDB 8.0 or later as a replica set: the one MONGOFLOW_BENCHMARKS_MONGO names, or a
// container started for the run.
await using var server = await BenchmarkServer.StartAsync(args);

var config = DefaultConfig.Instance.WithArtifactsPath(ArtifactsPath());
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

// Results land in benchmarks/artifacts wherever the run starts from: the repository root, the project or CI.
static string ArtifactsPath()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MongoFlow.sln")))
    {
        directory = directory.Parent;
    }

    return Path.Combine(directory?.FullName ?? Directory.GetCurrentDirectory(), "benchmarks", "artifacts");
}

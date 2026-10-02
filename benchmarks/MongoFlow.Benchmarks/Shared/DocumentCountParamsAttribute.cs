using BenchmarkDotNet.Attributes;

namespace MongoFlow.Benchmarks;

/// <summary>How many documents one save writes: one, a request's worth, and a batch.</summary>
public sealed class DocumentCountParamsAttribute() : ParamsAttribute(1, 100, 1000);

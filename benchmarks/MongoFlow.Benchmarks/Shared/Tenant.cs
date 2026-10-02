namespace MongoFlow.Benchmarks;

/// <summary>The request's tenant: a scoped service the multi-tenancy feature reads.</summary>
public sealed class Tenant
{
    public const string Current = "tenant-1";

    public string Id { get; } = Current;
}

namespace MongoFlow.Benchmarks;

/// <summary>A document every built-in feature applies to, when it's added.</summary>
public sealed class Order : ISoftDeletable, IVersioned, ITenantOwned
{
    public int Id { get; set; }

    public string Customer { get; set; } = "";

    public decimal Total { get; set; }

    public int Version { get; set; }

    public bool IsDeleted { get; set; }

    public string? TenantId { get; set; }
}

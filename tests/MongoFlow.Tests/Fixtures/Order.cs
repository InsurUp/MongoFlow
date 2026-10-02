namespace MongoFlow.Tests;

public sealed class Order : ISoftDeletable, ITenantOwned
{
    public int Id { get; set; }

    public string Customer { get; set; } = "";

    public decimal Total { get; set; }

    public bool IsDeleted { get; set; }

    public string? TenantId { get; set; }
}

namespace MongoFlow.Benchmarks;

public interface ITenantOwned
{
    string? TenantId { get; set; }
}

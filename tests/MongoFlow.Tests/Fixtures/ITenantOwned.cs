namespace MongoFlow.Tests;

public interface ITenantOwned
{
    string? TenantId { get; set; }
}

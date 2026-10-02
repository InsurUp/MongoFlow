namespace MongoFlow.IntegrationTests;

public sealed class ShopVault : MongoVault
{
    public IVaultCollection<Order, int> Orders { get; init; } = null!;

    public IVaultCollection<AuditEntry> Audit { get; init; } = null!;
}

namespace MongoFlow.IntegrationTests;

/// <summary>A second vault, for saves that join one transaction.</summary>
public sealed class LedgerVault : MongoVault
{
    public IVaultCollection<LedgerEntry, int> Entries { get; init; } = null!;
}

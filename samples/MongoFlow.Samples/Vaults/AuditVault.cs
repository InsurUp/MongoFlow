using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public sealed class AuditVault : MongoVault, IAuditVault
{
    public IVaultCollection<AuditLogEntry> Entries { get; init; } = null!;
}

using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public interface IAuditVault : IMongoVault
{
    IVaultCollection<AuditLogEntry> Entries { get; }
}

public sealed class AuditVault : MongoVault, IAuditVault
{
    public IVaultCollection<AuditLogEntry> Entries { get; init; } = null!;
}

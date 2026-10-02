using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public interface IAuditVault : IMongoVault
{
    IVaultCollection<AuditLogEntry> Entries { get; }
}

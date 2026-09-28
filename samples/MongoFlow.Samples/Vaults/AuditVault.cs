using MongoDB.Driver;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public interface IAuditVault : IMongoVault
{
    IVaultCollection<AuditLogEntry> Entries { get; }
}

public sealed class AuditVault : MongoVault, IAuditVault, IConfigurableVault<AuditVault>
{
    public IVaultCollection<AuditLogEntry> Entries { get; init; } = null!;

    // Audit entries suit a time-series collection that drops them after the retention period.
    public static void Configure(IVaultBuilder<AuditVault> vault) => vault
        .Collection(x => x.Entries, entries => entries.CreateWith(o =>
        {
            o.TimeSeriesOptions = new TimeSeriesOptions(nameof(AuditLogEntry.At));
            o.ExpireAfter = TimeSpan.FromDays(400);
        }));
}

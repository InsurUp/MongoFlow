using MongoDB.Driver.Linq;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>Keyless collections can be queried and appended to, but have no key operations.</summary>
public sealed class AuditReader(IAuditVault vault)
{
    public async Task<List<AuditLogEntry>> RecentAsync(string collection, CancellationToken cancellationToken)
    {
        var entries = await vault.Entries.QueryAsync(cancellationToken);

        return await entries
            .Where(entry => entry.Collection == collection)
            .OrderByDescending(entry => entry.At)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task PruneAsync(DateTime before, CancellationToken cancellationToken)
    {
        vault.Entries.DeleteMany(entry => entry.At < before);
        await vault.SaveAsync(cancellationToken);
    }
}

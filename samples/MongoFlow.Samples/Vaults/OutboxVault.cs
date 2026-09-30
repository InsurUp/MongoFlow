using MongoDB.Bson;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public sealed class OutboxVault : MongoVault, IOutboxVault
{
    public IVaultCollection<OutboxMessage, ObjectId> Messages { get; init; } = null!;
}

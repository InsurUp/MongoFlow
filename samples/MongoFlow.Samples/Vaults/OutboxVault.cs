using MongoDB.Bson;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public interface IOutboxVault : IMongoVault
{
    IVaultCollection<OutboxMessage, ObjectId> Messages { get; }
}

public sealed class OutboxVault : MongoVault, IOutboxVault
{
    public IVaultCollection<OutboxMessage, ObjectId> Messages { get; init; } = null!;
}

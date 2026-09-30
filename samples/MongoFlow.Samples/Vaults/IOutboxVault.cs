using MongoDB.Bson;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

public interface IOutboxVault : IMongoVault
{
    IVaultCollection<OutboxMessage, ObjectId> Messages { get; }
}

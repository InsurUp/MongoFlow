using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>Stands in for MongoFlow.Encryption's encryption: reads and writes through the vault's client, unchanged.</summary>
internal sealed class PlainEncryption : VaultEncryption
{
    public override IMongoDatabase Database(IMongoDatabase source) => source;

    public override ICollectionEncryption ForCollection<TDocument>(CollectionModel<TDocument> collection,
        IReadOnlyList<EncryptedMember> members) =>
        new PlainCollectionEncryption([.. members.Select(member => member.Path)]);

    public override Task PrepareAsync(VaultModel model,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public override Task PrepareCollectionAsync(VaultModel model,
        ICollectionModel collection,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

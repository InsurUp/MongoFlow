using MongoDB.Bson;

namespace MongoFlow.Tests;

/// <summary>Stands in for a collection's encryption: what it's handed comes back as it is.</summary>
internal sealed class PlainCollectionEncryption(IReadOnlyList<string> paths) : ICollectionEncryption
{
    public IReadOnlyList<string> Paths { get; } = paths;

    public bool SingleDocumentWrites => false;

    public ValueTask EnsureReadyAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask<BsonDocument> EncryptDocumentAsync(BsonDocument document,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(document);

    public ValueTask<BsonDocument> EncryptFilterAsync(BsonDocument filter,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(filter);

    public BsonDocument EncryptFilter(BsonDocument filter) => filter;

    public ValueTask<BsonValue> EncryptUpdateAsync(BsonValue update,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(update);
}

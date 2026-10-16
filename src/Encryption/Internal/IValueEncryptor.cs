using MongoDB.Bson;
using MongoDB.Driver.Encryption;

namespace MongoFlow.Encryption;

/// <summary>Encrypts values and range expressions, as <see cref="ClientEncryption"/> does: asynchronously, or not.</summary>
internal interface IValueEncryptor
{
    ValueTask<BsonValue> EncryptAsync(BsonValue value,
        EncryptOptions options,
        CancellationToken cancellationToken);

    ValueTask<BsonDocument> EncryptExpressionAsync(BsonDocument expression,
        EncryptOptions options,
        CancellationToken cancellationToken);
}

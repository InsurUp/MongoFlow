using MongoDB.Bson;
using MongoDB.Driver.Encryption;

namespace MongoFlow.Encryption;

/// <summary>
/// Encrypts with a <see cref="ClientEncryption"/>; <paramref name="synchronous"/> for LINQ's synchronous execution, when its
/// tasks complete before they're returned.
/// </summary>
internal sealed class ClientEncryptor(ClientEncryption encryption,
    bool synchronous) : IValueEncryptor
{
    public async ValueTask<BsonValue> EncryptAsync(BsonValue value,
        EncryptOptions options,
        CancellationToken cancellationToken) =>
        synchronous
            ? encryption.Encrypt(value, options, cancellationToken)
            : await encryption.EncryptAsync(value, options, cancellationToken);

    public async ValueTask<BsonDocument> EncryptExpressionAsync(BsonDocument expression,
        EncryptOptions options,
        CancellationToken cancellationToken) =>
        synchronous
            ? encryption.EncryptExpression(expression, options, cancellationToken)
            : await encryption.EncryptExpressionAsync(expression, options, cancellationToken);
}

using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>A key on a KMIP server, which <see cref="KmipKms"/> creates data keys under; without one, the server creates one.</summary>
public sealed class KmipMasterKey
{
    /// <summary>The key's unique identifier on the server; the server creates a key without.</summary>
    public string? KeyId { get; init; }

    /// <summary>A KMIP server other than the provider's, for this key.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Whether the server encrypts and decrypts the data keys itself, rather than handing out the key.</summary>
    public bool Delegated { get; init; }

    internal BsonDocument ToDocument() =>
        new BsonDocument()
            .AddIfNotNull("keyId", KeyId)
            .AddIfNotNull("endpoint", Endpoint)
            .Add("delegated", Delegated, Delegated);
}

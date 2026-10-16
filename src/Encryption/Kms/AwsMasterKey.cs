using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>A customer master key in AWS KMS, which <see cref="AwsKms"/> creates data keys under.</summary>
public sealed class AwsMasterKey
{
    /// <param name="region">The key's region, such as <c>eu-central-1</c>.</param>
    /// <param name="key">The key's ARN, such as <c>arn:aws:kms:eu-central-1:111122223333:key/...</c>.</param>
    public AwsMasterKey(string region,
        string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Region = region;
        Key = key;
    }

    /// <summary>The key's region.</summary>
    public string Region { get; }

    /// <summary>The key's ARN.</summary>
    public string Key { get; }

    /// <summary>A KMS endpoint other than the region's, such as a VPC endpoint.</summary>
    public string? Endpoint { get; init; }

    internal BsonDocument ToDocument() =>
        new BsonDocument { { "region", Region }, { "key", Key } }.AddIfNotNull("endpoint", Endpoint);
}

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver.Encryption;
using MongoFlow.Encryption;

namespace MongoFlow.Tests.Encryption;

public partial class FieldEncryptionTests
{
    private static readonly Guid SsnKey = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SalaryKey = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid NotesKey = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid StreetKey = Guid.Parse("00000000-0000-0000-0000-000000000004");

    /// <summary>
    /// Stands in for <see cref="ClientEncryption"/>: an encrypted value or range expression shows what it was made from,
    /// how, and with which data key, so a test reads what was encrypted.
    /// </summary>
    private sealed class ShowingEncryptor : IValueEncryptor
    {
        public ValueTask<BsonValue> EncryptAsync(BsonValue value,
            EncryptOptions options,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<BsonValue>(new BsonDocument
            {
                { "encrypted", value },
                { "algorithm", options.Algorithm },
                { "queryType", (BsonValue?)options.QueryType ?? BsonNull.Value },
                { "key", options.KeyId!.Value.ToString()[^1..] }
            });

        public ValueTask<BsonDocument> EncryptExpressionAsync(BsonDocument expression,
            EncryptOptions options,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BsonDocument
            {
                { "encryptedExpression", expression },
                { "queryType", options.QueryType },
                { "key", options.KeyId!.Value.ToString()[^1..] }
            });
    }

    /// <summary>Customers' encryption: Ssn for equality, Salary for ranges, Notes and Address.Street without queries.</summary>
    private static FieldEncryption Encryption()
    {
        var fields = new List<FieldSchema>
        {
            Field("Ssn", new StringSerializer(), EncryptedQuery.Equality),
            Field("Salary", new Int32Serializer(), EncryptedQuery.Range(0, 1_000_000)),
            Field("Notes", new StringSerializer(), null),
            Field("Address.Street", new StringSerializer(), null)
        };

        var keys = new Dictionary<string, Guid>
        {
            ["Ssn"] = SsnKey,
            ["Salary"] = SalaryKey,
            ["Notes"] = NotesKey,
            ["Address.Street"] = StreetKey
        };

        return new FieldEncryption("Customers", fields, keys, new ShowingEncryptor());
    }

    private static FieldSchema Field(string path,
        IBsonSerializer serializer,
        EncryptedQuery? query) =>
        FieldSchema.Of(new EncryptedMember(path, serializer, new EncryptedField(query, null)), "Customers");
}

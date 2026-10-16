using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoFlow.Encryption;

namespace MongoFlow.Tests.Encryption;

/// <summary>
/// <see cref="FieldSchema"/> describes an encrypted member as the server's schema holds it:
/// <list type="number">
/// <item>its BSON type comes from its serializer, a nullable member's from its value's, unless the declaration gives it;</item>
/// <item>a range takes a type that can be ordered, bounds of the member's type, and a precision only for a double or a
/// decimal;</item>
/// <item>a stored schema matches when everything but the data key is the same, MongoDB's defaults standing for what it
/// leaves out.</item>
/// </list>
/// </summary>
public class FieldSchemaTests
{
    [Test]
    public async Task ToDocument_Members_DescribeThemFromTheirSerializers()
    {
        // Act
        var fields = new[]
        {
            Of("Ssn", new StringSerializer(), EncryptedQuery.EqualityWith(4)),
            Of("Salary", new NullableSerializer<int>(new Int32Serializer()), EncryptedQuery.Range(0, 1_000_000)),
            Of("BornAt", new DateTimeSerializer(), EncryptedQuery.Range(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc))),
            Of("Rate", new DecimalSerializer(BsonType.Decimal128), EncryptedQuery.Range(0m, 1m, precision: 4, sparsity: 1, trimFactor: 2)),
            Of("Address", BsonSerializer.LookupSerializer<Order>(), null),
            Of("Tags", new ArraySerializer<string>(), null),
            Of("Photo", new ObjectSerializer(), null, BsonType.Binary)
        }.Select(field => field.ToDocument(BsonNull.Value));

        // Assert
        await Verify(fields);
    }

    [Test]
    [Arguments("RangeOfAString")]
    [Arguments("DoubleRangeWithoutPrecision")]
    [Arguments("IntRangeWithPrecision")]
    [Arguments("BoundOfAnotherType")]
    [Arguments("TypeTheSerializerDoesntTell")]
    [Arguments("TypeMongoDbCantEncrypt")]
    public async Task Of_DeclarationMongoDbCantEncrypt_ThrowsVaultConfigurationException(string declaration)
    {
        // Arrange
        Func<FieldSchema> of = declaration switch
        {
            "RangeOfAString" => () => Of("Ssn", new StringSerializer(), EncryptedQuery.Range(0, 10)),
            "DoubleRangeWithoutPrecision" => () => Of("Rate", new DoubleSerializer(), EncryptedQuery.Range(0d, 1d)),
            "IntRangeWithPrecision" => () => Of("Salary", new Int32Serializer(), EncryptedQuery.Range(0, 10, precision: 2)),
            "BoundOfAnotherType" => () => Of("Salary", new Int32Serializer(), EncryptedQuery.Range(0L, 10L)),
            "TypeTheSerializerDoesntTell" => () => Of("Photo", new ObjectSerializer(), null),
            _ => () => Of("Missing", new StringSerializer(), null, BsonType.Null)
        };

        // Act & Assert
        await Throws(of).IgnoreStackTrace().UseParameters(declaration);
    }

    [Test]
    public async Task Matches_StoredFields_ComparesAllButTheDataKey()
    {
        // Arrange — the server may leave out what MongoDB defaults, and hold queries as an array of one.
        var salary = Of("Salary", new Int32Serializer(), EncryptedQuery.Range(0, 100));
        var ssn = Of("Ssn", new StringSerializer(), EncryptedQuery.Equality);

        // Act
        var matches = new
        {
            Same = salary.Matches(Stored("{ keyId: 1, path: 'Salary', bsonType: 'int', queries: { queryType: 'range', contention: 8, min: 0, max: 100, sparsity: 2, trimFactor: 6 } }")),
            Defaulted = salary.Matches(Stored("{ path: 'Salary', bsonType: 'int', queries: [{ queryType: 'range', min: 0, max: 100 }] }")),
            OtherBounds = salary.Matches(Stored("{ path: 'Salary', bsonType: 'int', queries: { queryType: 'range', min: 0, max: 50 } }")),
            OtherType = ssn.Matches(Stored("{ path: 'Ssn', bsonType: 'long', queries: { queryType: 'equality' } }")),
            OtherContention = ssn.Matches(Stored("{ path: 'Ssn', bsonType: 'string', queries: { queryType: 'equality', contention: 2 } }")),
            WithoutQueries = ssn.Matches(Stored("{ path: 'Ssn', bsonType: 'string' }")),
            QueriedWhenNotDeclared = Of("Notes", new StringSerializer(), null)
                .Matches(Stored("{ path: 'Notes', bsonType: 'string', queries: { queryType: 'equality' } }"))
        };

        // Assert
        await Verify(matches);
    }

    private static BsonDocument Stored(string json) => BsonDocument.Parse(json);

    private static FieldSchema Of(string path,
        IBsonSerializer serializer,
        EncryptedQuery? query,
        BsonType? bsonType = null) =>
        FieldSchema.Of(new EncryptedMember(path, serializer, new EncryptedField(query, bsonType)), "Customers");
}

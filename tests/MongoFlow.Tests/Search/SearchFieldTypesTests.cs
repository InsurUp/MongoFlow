using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchFieldTypes.Of"/> reads off a feature field's serializer the mapping that compares its values exactly:
/// by the representation it writes, through nullable values; for a serializer of an app's own, a string as a token and a
/// value type by what it writes its default as; and none for what a search can't compare exactly, such as decimals,
/// documents and GUIDs of no set representation.
/// </summary>
public partial class SearchFieldTypesTests
{
    [Test]
    [MethodDataSource(nameof(Serializers))]
    public async Task Of_Serializer_IsTheMappingItsValuesCompareWith(string serializer,
        IBsonSerializer instance,
        string? expected)
    {
        // Act
        var type = SearchFieldTypes.Of(instance);

        // Assert
        await Assert.That(type).IsEqualTo(expected);
    }

    public static IEnumerable<Func<(string, IBsonSerializer, string?)>> Serializers()
    {
        yield return () => ("string", new StringSerializer(), "token");
        yield return () => ("string as ObjectId", new StringSerializer(BsonType.ObjectId), "objectId");
        yield return () => ("ObjectId", new ObjectIdSerializer(), "objectId");
        yield return () => ("bool", new BooleanSerializer(), "boolean");
        yield return () => ("int", new Int32Serializer(), "number");
        yield return () => ("long", new Int64Serializer(), "number");
        yield return () => ("double", new DoubleSerializer(), "number");
        yield return () => ("decimal", new DecimalSerializer(), null);
        yield return () => ("enum", new EnumSerializer<DayOfWeek>(), "number");
        yield return () => ("enum as string", new EnumSerializer<DayOfWeek>(BsonType.String), "token");
        yield return () => ("nullable DateTime", new NullableSerializer<DateTime>(new DateTimeSerializer()), "date");
        yield return () => ("DateTimeOffset", new DateTimeOffsetSerializer(), null);
        yield return () => ("standard Guid", new GuidSerializer(GuidRepresentation.Standard), "uuid");
        yield return () => ("Guid as string", new GuidSerializer(BsonType.String), "token");
        yield return () => ("Guid of no representation", new GuidSerializer(), null);
        yield return () => ("document", BsonSerializer.LookupSerializer<BsonDocument>(), null);
        yield return () => ("own string", new OwnSerializer<string>(new StringSerializer()), "token");
        yield return () => ("own bool", new OwnSerializer<bool>(new BooleanSerializer()), "boolean");
        yield return () => ("own long", new OwnSerializer<long>(new Int64Serializer()), "number");
        yield return () => ("own DateTime", new OwnSerializer<DateTime>(new DateTimeSerializer()), "date");
        yield return () => ("own ObjectId", new OwnSerializer<ObjectId>(new ObjectIdSerializer()), "objectId");
        yield return () => ("own decimal", new OwnSerializer<decimal>(new DecimalSerializer()), null);
        yield return () => ("own class", new OwnSerializer<Version>(new VersionSerializer()), null);
        yield return () => ("strongly typed id", new NullableSerializer<AgencyId>(new AgencyIdSerializer()), "uuid");
        yield return () => ("own TimeSpan it can't write", new UnwritableSerializer(), null);
    }
}

using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Semver;

namespace MongoFlow.Bson.Serializers;

internal sealed class SemVersionSerializer : SerializerBase<SemVersion>
{
    public override SemVersion Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonReader = context.Reader;
        var type = bsonReader.GetCurrentBsonType();

        return type switch
        {
            BsonType.String => SemVersion.Parse(bsonReader.ReadString()),
            _ => throw CreateCannotDeserializeFromBsonTypeException(type)
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, SemVersion value)
    {
        var bsonWriter = context.Writer;
        bsonWriter.WriteString(value.ToString());
    }
}
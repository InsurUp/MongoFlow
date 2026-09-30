using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Semver;

namespace MongoFlow;

internal sealed class SemVersionSerializer : SerializerBase<SemVersion>
{
    public override SemVersion Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();

        return type == BsonType.String
            ? SemVersion.Parse(context.Reader.ReadString())
            : throw CreateCannotDeserializeFromBsonTypeException(type);
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, SemVersion value) =>
        context.Writer.WriteString(value.ToString());
}

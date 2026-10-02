using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>Stores an <see cref="AgencyId"/> as its <see cref="Guid"/>. The driver needs one for each strongly typed id.</summary>
public sealed class AgencyIdSerializer : StructSerializerBase<AgencyId>
{
    private static readonly GuidSerializer Guid = new(GuidRepresentation.Standard);

    public override AgencyId Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        new(Guid.Deserialize(context, args));

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, AgencyId value) =>
        Guid.Serialize(context, args, value.Value);
}

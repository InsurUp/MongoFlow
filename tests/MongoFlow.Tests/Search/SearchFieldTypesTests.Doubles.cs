using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace MongoFlow.Tests.Search;

public partial class SearchFieldTypesTests
{
    /// <summary>A strongly typed id, stored as a standard UUID by a serializer of its own.</summary>
    public readonly record struct AgencyId(Guid Value);

    /// <summary>Stands in for a serializer of an app's own, which doesn't say what it writes: it writes as another does.</summary>
    private sealed class OwnSerializer<T>(IBsonSerializer<T> writes) : SerializerBase<T>
    {
        public override void Serialize(BsonSerializationContext context,
            BsonSerializationArgs args,
            T value) =>
            writes.Serialize(context, args, value);
    }

    /// <summary>Stands in for a strongly typed id's serializer, writing the id's GUID as a standard UUID.</summary>
    private sealed class AgencyIdSerializer : StructSerializerBase<AgencyId>
    {
        private static readonly GuidSerializer Guid = new(GuidRepresentation.Standard);

        public override void Serialize(BsonSerializationContext context,
            BsonSerializationArgs args,
            AgencyId value) =>
            Guid.Serialize(context, args, value.Value);
    }

    /// <summary>Stands in for a serializer of an app's own that can't write a value type's default.</summary>
    private sealed class UnwritableSerializer : SerializerBase<TimeSpan>;
}

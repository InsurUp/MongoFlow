using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace MongoFlow;

/// <summary>
/// The search mapping a field needs for a search's filters to compare it exactly, read off its serializer: what MongoFlow
/// maps the fields its features filter on as.
/// </summary>
internal static class SearchFieldTypes
{
    /// <summary>
    /// <c>token</c>, <c>objectId</c>, <c>boolean</c>, <c>number</c>, <c>date</c> or <c>uuid</c>, by what
    /// <paramref name="serializer"/> writes; <see langword="null"/> for anything else, such as a document or a decimal, which
    /// a search can't compare exactly.
    /// </summary>
    public static string? Of(IBsonSerializer serializer)
    {
        while (serializer is IChildSerializerConfigurable { ChildSerializer: { } child } && serializer.ValueType.IsGenericType &&
               serializer.ValueType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            serializer = child;
        }

        if (serializer is GuidSerializer guid)
        {
            return guid.Representation switch
            {
                BsonType.String => "token",
                BsonType.Binary when guid.GuidRepresentation == GuidRepresentation.Standard => "uuid",
                _ => null
            };
        }

        if (serializer is IRepresentationConfigurable configurable)
        {
            return Of(configurable.Representation);
        }

        // A serializer of the app's own, such as one of a strongly typed id, doesn't say what it writes: a string is taken
        // to be written as one, and a value type is written once, as its default, to see.
        return serializer.ValueType == typeof(string) ? "token"
            : serializer.ValueType.IsValueType ? Written(serializer)
            : null;
    }

    private static string? Of(BsonType type) =>
        type switch
        {
            BsonType.String => "token",
            BsonType.ObjectId => "objectId",
            BsonType.Boolean => "boolean",
            BsonType.Int32 or BsonType.Int64 or BsonType.Double => "number",
            BsonType.DateTime => "date",
            _ => null
        };

    // What the serializer writes a value type's default as; null when it can't write it.
    private static string? Written(IBsonSerializer serializer)
    {
        var written = new BsonDocument();
        try
        {
            using var writer = new BsonDocumentWriter(written);
            writer.WriteStartDocument();
            writer.WriteName("value");
            serializer.Serialize(BsonSerializationContext.CreateRoot(writer), Activator.CreateInstance(serializer.ValueType));
            writer.WriteEndDocument();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }

        return written["value"] is BsonBinaryData { SubType: BsonBinarySubType.UuidStandard } ? "uuid" : Of(written["value"].BsonType);
    }
}

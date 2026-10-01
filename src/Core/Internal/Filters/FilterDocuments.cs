using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Filters built as BSON documents. A write by key or token is then a few elements, instead of a new expression the
/// driver translates for every write.
/// </summary>
internal static class FilterDocuments
{
    /// <summary>Renders filters and fields for <paramref name="collection"/> the way the driver renders its writes.</summary>
    public static RenderArgs<TDocument> RenderArgs<TDocument>(IMongoCollection<TDocument> collection) =>
        new(collection.DocumentSerializer,
            collection.Settings.SerializerRegistry,
            translationOptions: collection.Database.Client.Settings.TranslationOptions);

    /// <summary>The element name and serializer of the member <c>x =&gt; x.Member</c> reads.</summary>
    public static RenderedFieldDefinition RenderField<TDocument>(LambdaExpression member,
        RenderArgs<TDocument> args) =>
        new ExpressionFieldDefinition<TDocument>(member).Render(args);

    /// <summary><c>field: value</c>, with the value serialized like the field.</summary>
    public static BsonElement Equal(RenderedFieldDefinition field,
        object? value) =>
        new(field.FieldName, field.FieldSerializer.ToBsonValue(value));

    /// <summary>
    /// Joins filters into one, with their fields side by side, which the server matches faster than <c>$and</c>; or with
    /// <c>$and</c> when two of them constrain the same field. Null filters are dropped. Returns <see langword="null"/>
    /// when nothing is left to filter by, and the one filter left as it is. The filters aren't changed: a save shares its
    /// rendered query filters between operations.
    /// </summary>
    public static BsonDocument? And(params ReadOnlySpan<BsonDocument?> filters)
    {
        BsonDocument? first = null;
        BsonDocument? joined = null;

        foreach (var filter in filters)
        {
            if (filter is null)
            {
                continue;
            }

            if (first is null)
            {
                first = filter;
                continue;
            }

            joined ??= new BsonDocument(first);

            foreach (var element in filter)
            {
                if (joined.Contains(element.Name))
                {
                    return AndEach(filters);
                }

                joined.Add(element);
            }
        }

        return joined ?? first;
    }

    private static BsonDocument AndEach(ReadOnlySpan<BsonDocument?> filters)
    {
        var all = new BsonArray();

        foreach (var filter in filters)
        {
            if (filter is not null)
            {
                all.Add(filter);
            }
        }

        return new BsonDocument("$and", all);
    }
}

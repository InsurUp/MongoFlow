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
    /// Joins filters with <c>$and</c>. Null filters are dropped. Returns <see langword="null"/> when nothing is left to
    /// filter by, and the one filter left as it is.
    /// </summary>
    public static BsonDocument? And(params ReadOnlySpan<BsonDocument?> filters)
    {
        BsonDocument? first = null;
        BsonArray? all = null;

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

            all ??= new BsonArray { first };
            all.Add(filter);
        }

        return all is null ? first : new BsonDocument("$and", all);
    }
}

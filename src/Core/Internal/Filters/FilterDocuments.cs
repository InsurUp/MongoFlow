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
    /// <remarks>Elements are read by index: enumerating a <see cref="BsonDocument"/> allocates an enumerator.</remarks>
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

            if (joined is null)
            {
                joined = [];
                for (var i = 0; i < first.ElementCount; i++)
                {
                    joined.Add(first.GetElement(i));
                }
            }

            for (var i = 0; i < filter.ElementCount; i++)
            {
                var element = filter.GetElement(i);
                if (joined.Contains(element.Name))
                {
                    return AndEach(filters);
                }

                joined.Add(element);
            }
        }

        return joined ?? first;
    }

    /// <summary>
    /// Whether <paramref name="filter"/> compares a field that reaches one of <paramref name="paths"/>: the field itself, a
    /// field inside it, or a document that holds it, at its top level, under <c>$and</c>, <c>$or</c> and <c>$nor</c>, or
    /// referred to in <c>$expr</c>.
    /// </summary>
    public static bool Touches(BsonDocument filter,
        IReadOnlyList<string> paths)
    {
        for (var i = 0; i < filter.ElementCount; i++)
        {
            var (name, value) = (filter.GetElement(i).Name, filter.GetElement(i).Value);

            var touches = name switch
            {
                "$and" or "$or" or "$nor" => value is BsonArray clauses &&
                                             clauses.Any(clause => clause is BsonDocument document && Touches(document, paths)),
                "$expr" => RefersTo(value, paths),
                _ when name.StartsWith('$') => false,
                _ => Reaches(name, paths)
            };

            if (touches)
            {
                return true;
            }
        }

        return false;
    }

    // Whether an aggregation expression refers to a field, as "$path", that reaches one of the paths.
    private static bool RefersTo(BsonValue expression,
        IReadOnlyList<string> paths) =>
        expression switch
        {
            BsonString { Value: ['$', not '$', ..] field } => Reaches(field[1..], paths),
            BsonDocument document => document.Values.Any(value => RefersTo(value, paths)),
            BsonArray array => array.Any(value => RefersTo(value, paths)),
            _ => false
        };

    private static bool Reaches(string field,
        IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (ElementPaths.Reaches(field, path))
            {
                return true;
            }
        }

        return false;
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

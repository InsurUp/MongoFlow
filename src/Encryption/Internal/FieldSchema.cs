using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver.Encryption;

namespace MongoFlow.Encryption;

/// <summary>
/// An encrypted member as the server's schema describes it: its path, its BSON type and the queries it allows. Written
/// into the encrypted collection when it's created, and compared with what the server holds.
/// </summary>
internal sealed class FieldSchema
{
    private const int DefaultSparsity = 2;
    private const int DefaultTrimFactor = 6;

    private static readonly Dictionary<BsonType, string> TypeNames = new()
    {
        [BsonType.Double] = "double",
        [BsonType.String] = "string",
        [BsonType.Document] = "object",
        [BsonType.Array] = "array",
        [BsonType.Binary] = "binData",
        [BsonType.ObjectId] = "objectId",
        [BsonType.Boolean] = "bool",
        [BsonType.DateTime] = "date",
        [BsonType.RegularExpression] = "regex",
        [BsonType.JavaScript] = "javascript",
        [BsonType.Symbol] = "symbol",
        [BsonType.Int32] = "int",
        [BsonType.Timestamp] = "timestamp",
        [BsonType.Int64] = "long",
        [BsonType.Decimal128] = "decimal"
    };

    private static readonly HashSet<string> RangeTypes = ["int", "long", "date", "double", "decimal"];

    private FieldSchema(string path,
        string bsonType,
        EncryptedQuery? query,
        BsonValue? min,
        BsonValue? max)
    {
        Path = path;
        Segments = path.Split('.');
        TypeName = bsonType;
        Query = query;
        Min = min;
        Max = max;
    }

    public string Path { get; }

    /// <summary>The names along <see cref="Path"/>.</summary>
    public string[] Segments { get; }

    /// <summary>The server's name of its BSON type, such as <c>string</c> or <c>date</c>.</summary>
    public string TypeName { get; }

    public EncryptedQuery? Query { get; }

    public bool IsEquality => Query?.Type == "equality";

    public bool IsRange => Query?.Type == "range";

    private BsonValue? Min { get; }

    private BsonValue? Max { get; }

    /// <summary>The schema of <paramref name="member"/>, a member of <paramref name="collection"/> declared encrypted.</summary>
    /// <exception cref="VaultConfigurationException">Its BSON type can't be told, or doesn't suit its queries.</exception>
    public static FieldSchema Of(EncryptedMember member,
        string collection)
    {
        var field = (EncryptedField)member.Options;
        var serializer = Underlying(member.Serializer);
        var described = $"{member.Path}, in {collection},";

        var type = field.BsonType ?? TypeOf(serializer) ??
            throw new VaultConfigurationException(
                $"{described} is encrypted, but its serializer, {serializer.GetType().Name}, doesn't tell its BSON type. Give " +
                "it to Encrypt, such as bsonType: BsonType.String.");

        if (!TypeNames.TryGetValue(type, out var name))
        {
            throw new VaultConfigurationException($"{described} is stored as {type}, which MongoDB can't encrypt.");
        }

        if (field.Query is not { Type: "range" } range)
        {
            return new FieldSchema(member.Path, name, field.Query, null, null);
        }

        if (!RangeTypes.Contains(name))
        {
            throw new VaultConfigurationException(
                $"{described} is stored as {name}, so it can't be queried by range: a range takes an int, long, date, " +
                "double or decimal.");
        }

        var precise = name is "double" or "decimal";
        if (precise != range.Precision.HasValue)
        {
            throw new VaultConfigurationException(precise
                ? $"{described} is stored as {name}, so its range needs a precision: the digits after the point it keeps."
                : $"{described} is stored as {name}, so its range takes no precision: only a double or a decimal has one.");
        }

        return new FieldSchema(member.Path, name, range, Bound(serializer, range.Min, described), Bound(serializer, range.Max, described));
    }

    /// <summary>
    /// The field as the server's schema holds it, with <paramref name="keyId"/>: its data key's id, or null for
    /// <c>CreateEncryptedCollectionAsync</c> to create one.
    /// </summary>
    public BsonDocument ToDocument(BsonValue keyId)
    {
        var field = new BsonDocument { { "path", Path }, { "bsonType", TypeName }, { "keyId", keyId } };

        if (Query is { } query)
        {
            var queries = new BsonDocument { { "queryType", query.Type }, { "contention", query.Contention } };
            if (IsRange)
            {
                queries.Add("min", Min).Add("max", Max).Add("sparsity", query.Sparsity).Add("trimFactor", query.TrimFactor);
                if (query.Precision is { } precision)
                {
                    queries.Add("precision", precision);
                }
            }

            field.Add("queries", queries);
        }

        return field;
    }

    /// <summary>Whether <paramref name="stored"/>, the server's schema of the field, is the same, its data key aside.</summary>
    public bool Matches(BsonDocument stored)
    {
        if (stored.GetValue("bsonType", BsonNull.Value) != TypeName)
        {
            return false;
        }

        var queries = stored.GetValue("queries", BsonNull.Value) switch
        {
            BsonDocument document => document,
            BsonArray { Count: 1 } array => array[0] as BsonDocument,
            _ => null
        };

        if (Query is not { } query)
        {
            return queries is null;
        }

        return queries is not null &&
               queries.GetValue("queryType", BsonNull.Value) == query.Type &&
               Number(queries, "contention", EncryptedQuery.DefaultContention) == query.Contention &&
               (!IsRange ||
                (queries.GetValue("min", BsonNull.Value).Equals(Min) &&
                 queries.GetValue("max", BsonNull.Value).Equals(Max) &&
                 Number(queries, "sparsity", DefaultSparsity) == query.Sparsity &&
                 Number(queries, "trimFactor", DefaultTrimFactor) == query.TrimFactor &&
                 (queries.TryGetValue("precision", out var precision) ? precision.ToInt32() : (int?)null) == query.Precision));
    }

    /// <summary>How a value is encrypted to be stored: as the insert payload of its algorithm.</summary>
    public EncryptOptions StoreOptions(Guid keyId) =>
        Query switch
        {
            null => new EncryptOptions("Unindexed", keyId: keyId),
            { Type: "equality" } query => new EncryptOptions("Indexed", keyId: keyId, contentionFactor: query.Contention),
            var query => new EncryptOptions("Range", keyId: keyId, contentionFactor: query.Contention, rangeOptions: RangeOptions())
        };

    /// <summary>How a value a filter compares the field with is encrypted.</summary>
    public EncryptOptions FindOptions(Guid keyId) =>
        IsRange
            ? new EncryptOptions("Range", keyId: keyId, contentionFactor: Query!.Contention, queryType: "range",
                rangeOptions: RangeOptions())
            : new EncryptOptions("Indexed", keyId: keyId, contentionFactor: Query!.Contention, queryType: "equality");

    public override string ToString() => $"{Path} ({Describe()})";

    /// <summary>Its type and queries, such as <c>string, equality (contention 8)</c>.</summary>
    public string Describe() => $"{TypeName}, {Query?.ToString() ?? "without queries"}";

    /// <summary><see cref="Describe()"/> of <paramref name="stored"/>, the server's schema of a field.</summary>
    public static string Describe(BsonDocument stored)
    {
        var type = stored.GetValue("bsonType", "?");
        var queries = stored.GetValue("queries", BsonNull.Value) switch
        {
            BsonDocument document => document,
            BsonArray { Count: 1 } array => array[0] as BsonDocument,
            _ => null
        };

        if (queries is null)
        {
            return $"{type}, without queries";
        }

        var contention = Number(queries, "contention", EncryptedQuery.DefaultContention);
        return queries.GetValue("queryType", "?") == "range"
            ? $"{type}, range from {queries.GetValue("min", "?")} to {queries.GetValue("max", "?")} (contention {contention})"
            : $"{type}, {queries.GetValue("queryType", "?")} (contention {contention})";
    }

    private RangeOptions RangeOptions() =>
        new(min: Min, max: Max, precision: Query!.Precision, sparsity: Query.Sparsity, trimFactor: Query.TrimFactor);

    private static long Number(BsonDocument document,
        string name,
        long missing) =>
        document.TryGetValue(name, out var value) && value.IsNumeric ? value.ToInt64() : missing;

    // A nullable member's serializer serializes its value with another, which says how it's stored.
    private static IBsonSerializer Underlying(IBsonSerializer serializer) =>
        serializer.ValueType is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
        serializer is IChildSerializerConfigurable child
            ? child.ChildSerializer
            : serializer;

    private static BsonType? TypeOf(IBsonSerializer serializer) =>
        serializer switch
        {
            IRepresentationConfigurable { Representation: not 0 } representation => representation.Representation,
            IBsonArraySerializer => BsonType.Array,
            IBsonDocumentSerializer => BsonType.Document,
            _ => null
        };

    private static BsonValue Bound(IBsonSerializer serializer,
        object? bound,
        string described)
    {
        try
        {
            return serializer.ToBsonValue(bound);
        }
        catch (Exception exception) when (exception is InvalidCastException or BsonSerializationException or FormatException)
        {
            throw new VaultConfigurationException(
                $"{described} can't take a range bound of {bound} ({bound?.GetType().Name}): bounds are of the member's " +
                $"type. {exception.Message}");
        }
    }
}

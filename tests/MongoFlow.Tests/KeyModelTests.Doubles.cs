using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.Tests;

// Documents and keys of every shape a keyed collection can be configured with, and of the shapes it rejects.
public partial class KeyModelTests
{
    public sealed class Policy
    {
        public ObjectId Id { get; set; }

        [BsonElement("number")]
        public string Number { get; set; } = "";

        public string Product { get; set; } = "";

        public int Year { get; set; }
    }

    public enum ShipmentStatus
    {
        Created,
        InTransit
    }

    public sealed class Shipment
    {
        public int Id { get; set; }

        [BsonRepresentation(BsonType.String)]
        public ShipmentStatus Status { get; set; }
    }

    public sealed class Device
    {
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; }
    }

    public sealed class LoginToken
    {
        public int Id { get; set; }

        public string UserId { get; set; } = "";

        [BsonElement("provider")]
        public string Provider { get; set; } = "";
    }

    /// <summary>A keyless document, so it has no member mapped to <c>_id</c>.</summary>
    [BsonIgnoreExtraElements]
    public sealed class Note
    {
        public string Text { get; set; } = "";
    }

    public sealed record TokenKey(string UserId, string Provider);

    /// <summary>A composite key whose constructor parameters are camel case, like most hand-written constructors.</summary>
    public sealed class CamelCaseTokenKey(string userId, string provider)
    {
        public string UserId { get; } = userId;

        public string Provider { get; } = provider;
    }

    /// <summary>A key built with no arguments, so it names no member.</summary>
    public sealed class EmptyKey;

    /// <summary>A struct key, whose <c>new StructKey()</c> calls no constructor.</summary>
    public readonly struct StructKey;

    /// <summary>A key whose first constructor parameter isn't named like any of its properties.</summary>
    public sealed class RenamedKey(string user, string provider)
    {
        public string UserId { get; } = user;

        public string Provider { get; } = provider;
    }

    /// <summary>A key taking a <see cref="long"/>, so passing an <see cref="int"/> member converts it.</summary>
    public sealed record YearKey(long Year);

    /// <summary>A key taking the member's type, but holding it as another.</summary>
    public sealed class WideningKey(int year)
    {
        public long Year { get; } = year;
    }
}

using MongoDB.Bson;

namespace MongoFlow.Samples.Domain;

/// <summary>The tenants themselves: platform data, not owned by any agency.</summary>
public sealed class Agency
{
    public ObjectId Id { get; set; }

    public AgencyId AgencyId { get; set; }

    public required string Name { get; set; }

    public IReadOnlyList<string> Modules { get; set; } = [];
}

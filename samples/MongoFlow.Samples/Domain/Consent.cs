using MongoDB.Bson;

namespace MongoFlow.Samples.Domain;

public sealed class Consent : ITenantOwned
{
    public ObjectId Id { get; set; }

    public ObjectId CustomerId { get; init; }

    public ConsentPurpose Purpose { get; init; }

    public DateTimeOffset GivenAt { get; set; }

    public AgencyId? AgencyId { get; set; }
}

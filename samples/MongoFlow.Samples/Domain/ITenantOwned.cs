namespace MongoFlow.Samples.Domain;

public interface ITenantOwned
{
    AgencyId? AgencyId { get; set; }
}

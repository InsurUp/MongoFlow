using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.IntegrationTests;

// Documents owned by a tenant of each kind of id, one that isn't, and the request's tenant.
public partial class MultiTenancyTests
{
    public interface ITenantOwned
    {
        string? TenantId { get; set; }
    }

    public interface IAgencyOwned
    {
        Guid? AgencyId { get; set; }
    }

    /// <summary>Owned through a <see cref="Guid"/> that isn't nullable, so its default is the unset tenant.</summary>
    public interface IVisitOwned
    {
        Guid AgencyId { get; set; }
    }

    public sealed class Invoice : ITenantOwned
    {
        public int Id { get; set; }

        public string Number { get; set; } = "";

        public string? TenantId { get; set; }
    }

    public sealed class Quote : IAgencyOwned
    {
        public int Id { get; set; }

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid? AgencyId { get; set; }
    }

    public sealed class Visit : IVisitOwned
    {
        public int Id { get; set; }

        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid AgencyId { get; set; }
    }

    public sealed class Country
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    public sealed class TenantVault : MongoVault
    {
        public IVaultCollection<Invoice, int> Invoices { get; init; } = null!;

        public IVaultCollection<Quote, int> Quotes { get; init; } = null!;

        public IVaultCollection<Visit, int> Visits { get; init; } = null!;

        public IVaultCollection<Country, int> Countries { get; init; } = null!;
    }

    /// <summary>The request's tenant, as the app would resolve it.</summary>
    public sealed class CurrentTenant
    {
        public string? Id { get; init; }

        public Guid? Agency { get; init; }

        public bool All { get; init; }
    }
}

namespace MongoFlow.IntegrationTests;

// An agency's customers, their contracts and the payments on them, owned by an agency and removed softly, to join.
public partial class ReadTests
{
    public interface IAgencyOwned
    {
        string? AgencyId { get; set; }
    }

    public interface IRemovable
    {
        bool IsRemoved { get; set; }
    }

    public sealed class Customer : IAgencyOwned, IRemovable
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? AgencyId { get; set; }

        public bool IsRemoved { get; set; }
    }

    public sealed class Contract : IAgencyOwned, IRemovable
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }

        public string Number { get; set; } = "";

        public string? AgencyId { get; set; }

        public bool IsRemoved { get; set; }
    }

    public sealed class Payment : IAgencyOwned
    {
        public int Id { get; set; }

        public int ContractId { get; set; }

        public decimal Amount { get; set; }

        public string? AgencyId { get; set; }
    }

    public sealed class AgencyVault : MongoVault
    {
        public IVaultCollection<Customer, int> Customers { get; init; } = null!;

        public IVaultCollection<Contract, int> Contracts { get; init; } = null!;

        public IVaultCollection<Payment, int> Payments { get; init; } = null!;
    }
}

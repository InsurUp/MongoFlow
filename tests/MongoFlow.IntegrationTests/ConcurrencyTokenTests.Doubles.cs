using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.IntegrationTests;

// Documents with an int token under an element name of its own, a long token, a token and soft delete, and none.
public partial class ConcurrencyTokenTests
{
    public interface IVersioned
    {
        int Version { get; set; }
    }

    public interface IRevised
    {
        long Revision { get; set; }
    }

    public interface IArchivable
    {
        bool IsArchived { get; set; }
    }

    public sealed class Contract : IVersioned
    {
        public int Id { get; set; }

        public string Party { get; set; } = "";

        [BsonElement("ver")]
        public int Version { get; set; }
    }

    public sealed class Deal : IRevised
    {
        public int Id { get; set; }

        public long Revision { get; set; }
    }

    public sealed class ArchivedContract : IVersioned, IArchivable
    {
        public int Id { get; set; }

        public int Version { get; set; }

        public bool IsArchived { get; set; }
    }

    public sealed class Memo
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";
    }

    public sealed class ContractVault : MongoVault
    {
        public IVaultCollection<Contract, int> Contracts { get; init; } = null!;

        public IVaultCollection<Deal, int> Deals { get; init; } = null!;

        public IVaultCollection<ArchivedContract, int> Archives { get; init; } = null!;

        public IVaultCollection<Memo, int> Memos { get; init; } = null!;
    }
}

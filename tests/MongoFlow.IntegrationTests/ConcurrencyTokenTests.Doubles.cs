using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

// Documents with an int token under an element name of its own, a long token, a token and soft delete, and none; and
// an interceptor guarding writes with a condition of its own.
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

    /// <summary>Has a numeric member of its own next to its token, for updates that increment both.</summary>
    public sealed class Counter : IVersioned
    {
        public int Id { get; set; }

        public int Hits { get; set; }

        public int Version { get; set; }
    }

    public sealed class Memo
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";
    }

    /// <summary>Lets a contract update apply only to a stored contract of one party, as an interceptor's own guard.</summary>
    public sealed class PartyCondition(string party) : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var update in context.Operations.OfType<UpdateOperation<Contract>>())
            {
                update.AddCondition(Builders<Contract>.Filter.Eq(x => x.Party, party));
            }

            return ValueTask.CompletedTask;
        }
    }

    public sealed class ContractVault : MongoVault
    {
        public IVaultCollection<Contract, int> Contracts { get; init; } = null!;

        public IVaultCollection<Deal, int> Deals { get; init; } = null!;

        public IVaultCollection<ArchivedContract, int> Archives { get; init; } = null!;

        public IVaultCollection<Memo, int> Memos { get; init; } = null!;

        public IVaultCollection<Counter, int> Counters { get; init; } = null!;
    }
}

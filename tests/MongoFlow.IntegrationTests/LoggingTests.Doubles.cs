using MongoDB.Bson.Serialization.Attributes;

namespace MongoFlow.IntegrationTests;

// Vaults whose saves fail in each logged way, and one whose collections need indexes of every kind.
public partial class LoggingTests
{
    public interface ISoftDeletable
    {
        bool IsDeleted { get; set; }
    }

    public sealed class Ticket
    {
        public int Id { get; set; }

        public int Version { get; set; }
    }

    public sealed class VersionedVault : MongoVault
    {
        public IVaultCollection<Ticket, int> Tickets { get; init; } = null!;
    }

    public sealed class Bill
    {
        public int Id { get; set; }

        public string? TenantId { get; set; }
    }

    public sealed class TenantVault : MongoVault
    {
        public IVaultCollection<Bill, int> Bills { get; init; } = null!;
    }

    public sealed class Receipt
    {
        public int Id { get; set; }

        public string Code { get; set; } = "";
    }

    public sealed class Article : ISoftDeletable
    {
        public int Id { get; set; }

        public bool IsDeleted { get; set; }
    }

    public sealed class Page : ISoftDeletable
    {
        public int Id { get; set; }

        public string Slug { get; set; } = "";

        public bool IsDeleted { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class Draft : ISoftDeletable
    {
        public bool IsDeleted { get; set; }
    }

    /// <summary>Collections whose key or features need an index, in every state the check tells apart.</summary>
    public sealed class IndexedVault : MongoVault, IConfigurableVault<IndexedVault>
    {
        public IVaultCollection<Policy, string> Policies { get; init; } = null!;

        public IVaultCollection<LoginToken, TokenKey> Tokens { get; init; } = null!;

        public IVaultCollection<Receipt, string> Receipts { get; init; } = null!;

        public IVaultCollection<Article, int> Articles { get; init; } = null!;

        public IVaultCollection<Page, int> Pages { get; init; } = null!;

        public IVaultCollection<Bill, int> Bills { get; init; } = null!;

        public IVaultCollection<Draft> Drafts { get; init; } = null!;

        public static void Configure(IVaultBuilder<IndexedVault> vault) => vault
            .Collection(x => x.Policies, policies => policies.Key(p => p.Number))
            .Collection(x => x.Tokens, tokens => tokens.Key(t => new TokenKey(t.UserId, t.Provider)))
            .Collection(x => x.Receipts, receipts => receipts.Key(r => r.Code));
    }

    /// <summary>Throws from its failure hook, which the save swallows.</summary>
    public sealed class ThrowingOnFailure : VaultInterceptor
    {
        public override ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The failure hook failed.");
    }
}

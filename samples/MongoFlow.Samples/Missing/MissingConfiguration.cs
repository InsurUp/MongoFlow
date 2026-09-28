#if MISSING_API
// Vault setup a real app needs that the builder can't express yet. Build with -p:DefineConstants=MISSING_API to list
// every gap as a compiler error.

using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Missing;

public sealed class PolicyVaultMissingConfiguration : IVaultConfiguration<PolicyVault>
{
    public void Configure(IVaultBuilder<PolicyVault> vault) => vault
        .Collection(x => x.Policies, c => c
            // Indexes, created at startup or by a migration. A custom key almost always needs a unique one.
            .Index(index => index.Ascending(p => p.PolicyNumber).Unique())
            .Index(index => index.Ascending(p => p.AgencyId).Ascending(p => p.Status).Descending(p => p.StartsAt))
            // Optimistic concurrency: a replace fails if someone saved the policy after it was read.
            .ConcurrencyToken(p => p.Version)
            // Driver settings for one collection.
            .Settings(settings => settings.WriteConcern = WriteConcern.WMajority))
        .Collection(x => x.Claims, c => c.Index(index => index.Ascending(claim => claim.PolicyNumber)))
        // Interceptors, declaring what they need so each save can be planned before it starts.
        .AddInterceptor<TimestampInterceptor>()
        .AddInterceptor<AuditTrailInterceptor>(interceptor => interceptor
            .NeedsOriginals()
            .For(collection => collection.DocumentType != typeof(AuditLogEntry)))
        .AddInterceptor<OutboxInterceptor>()
        // Migrations for this vault only, not every migration in the assembly.
        .Migrations(migrations => migrations
            .FromNamespaceOf<PolicyMigrations.V1CreateCollections>()
            .Collection("__migrations"));
}

public sealed class AuditVaultMissingConfiguration : IVaultConfiguration<AuditVault>
{
    // Creation options: audit entries suit a time-series collection that drops old data.
    public void Configure(IVaultBuilder<AuditVault> vault) => vault
        .Collection(x => x.Entries, c => c.CreateWith(new CreateCollectionOptions
        {
            TimeSeriesOptions = new TimeSeriesOptions("At"),
            ExpireAfter = TimeSpan.FromDays(400)
        }));
}

public sealed class PlatformUserVaultMissingConfiguration : IVaultConfiguration<PlatformUserVault>
{
    // Composite keys: tokens are looked up by user and provider together.
    public void Configure(IVaultBuilder<PlatformUserVault> vault) => vault
        .Collection(x => x.Tokens, c => c.Key(token => token.UserId, token => token.Provider));
}

public sealed class TenancyVariants<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
{
    public void Configure(IVaultBuilder<TVault> vault)
    {
        // Soft delete that records when: UseSoftDelete only takes a bool member.
        vault.UseSoftDelete((IDeletedAt x) => x.DeletedAt);

        // Tenants identified by strings: UseMultiTenancy requires a struct tenant id.
        vault.UseMultiTenancy((IStringTenantOwned x) => x.TenantId, _ => "acme");

        // Platform admins see every tenant. There is no way to say "no tenant filter for this user": returning null
        // today means "only documents without a tenant".
        vault.UseMultiTenancy((ITenantOwned x) => x.AgencyId, services =>
        {
            var user = services.GetRequiredService<ICurrentUser>();
            return user.IsPlatformAdmin ? TenantScope.AllTenants : TenantScope.Only(user.AgencyId);
        });
    }
}

public interface IDeletedAt
{
    DateTime? DeletedAt { get; set; }
}

public interface IStringTenantOwned
{
    string? TenantId { get; set; }
}

public static class MissingRegistration
{
    public static void Register(IServiceCollection services)
    {
        // Transactions across vaults, which requires them to share a client.
        services.AddMongoVaultTransactions();

        // An identity package can't ship a default that reaches its generic base vault: defaults take one type
        // parameter, and IVaultCollection<TUser, ...> is invariant, so a constraint can't expose Users either.
        services.AddDefaultVaultConfiguration(typeof(Identity.UserVaultConfiguration<,>));
    }
}
#endif

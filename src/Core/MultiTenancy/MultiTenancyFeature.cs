using System.Linq.Expressions;

namespace MongoFlow;

public static class MultiTenancyFeature
{
    /// <summary>The key of the built-in multi-tenancy feature, added with <c>UseMultiTenancy</c>.</summary>
    public static FeatureKey Key { get; } = new("multi-tenancy");

    /// <summary>
    /// Adds the built-in multi-tenancy feature to every collection whose document is a
    /// <typeparamref name="TTenantEntity"/>: reads are limited to the current tenant, inserts and replaces without a
    /// tenant get the current one, and writes carrying another tenant are rejected.
    /// </summary>
    /// <remarks>
    /// With no current tenant, reads see only documents without a tenant, and writes are neither stamped nor checked.
    /// </remarks>
    /// <param name="vault">The vault to add the feature to.</param>
    /// <param name="tenantId">
    /// A settable member with a typed parameter, such as <c>(ITenantEntity x) =&gt; x.TenantId</c>, so the type
    /// arguments are inferred. An unset tenant is <see langword="null"/>, or the default value when the member isn't
    /// nullable.
    /// </param>
    /// <param name="currentTenantId">Called with the request's services whenever the current tenant is needed.</param>
    /// <param name="allTenants">
    /// Called with the request's services; when it returns <see langword="true"/>, such as for a platform admin, reads
    /// aren't limited to a tenant and writes aren't stamped or checked.
    /// </param>
    public static IVaultBuilder<TVault> UseMultiTenancy<TVault, TTenantEntity, TTenantId>(this IVaultBuilder<TVault> vault,
        Expression<Func<TTenantEntity, TTenantId?>> tenantId,
        Func<IServiceProvider, TTenantId?> currentTenantId,
        Func<IServiceProvider, bool>? allTenants = null)
        where TVault : MongoVault
        where TTenantId : struct =>
        vault.AddFeature(new MultiTenancyFeature<TTenantEntity, TTenantId?>(
            tenantId,
            currentTenantId,
            allTenants,
            tenant => tenant is null || EqualityComparer<TTenantId>.Default.Equals(tenant.Value, default)));

    /// <summary>
    /// Adds the built-in multi-tenancy feature for tenant ids of a reference type, such as <see cref="string"/>. It
    /// behaves like the overload for struct ids; an unset tenant is <see langword="null"/>.
    /// </summary>
    /// <param name="vault">The vault to add the feature to.</param>
    /// <param name="tenantId">A settable member with a typed parameter, such as <c>(ITenantEntity x) =&gt; x.TenantId</c>.</param>
    /// <param name="currentTenantId">Called with the request's services whenever the current tenant is needed.</param>
    /// <param name="allTenants">
    /// Called with the request's services; when it returns <see langword="true"/>, reads aren't limited to a tenant and
    /// writes aren't stamped or checked.
    /// </param>
    public static IVaultBuilder<TVault> UseMultiTenancy<TVault, TTenantEntity, TTenantId>(this IVaultBuilder<TVault> vault,
        Expression<Func<TTenantEntity, TTenantId?>> tenantId,
        Func<IServiceProvider, TTenantId?> currentTenantId,
        Func<IServiceProvider, bool>? allTenants = null)
        where TVault : MongoVault
        where TTenantId : class =>
        vault.AddFeature(new MultiTenancyFeature<TTenantEntity, TTenantId?>(
            tenantId,
            currentTenantId,
            allTenants,
            tenant => tenant is null));
}

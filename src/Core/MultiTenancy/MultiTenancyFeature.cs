using System.Linq.Expressions;

namespace MongoFlow;

public static class MultiTenancyFeature
{
    /// <summary>The key of the built-in multi-tenancy feature, added with <c>UseMultiTenancy</c>.</summary>
    public static FeatureKey Key { get; } = new("multi-tenancy");

    /// <summary>
    /// Adds the built-in multi-tenancy feature to every collection whose document is a
    /// <typeparamref name="TTenantEntity"/>: reads are limited to the current tenant, and inserts without a tenant get
    /// the current one.
    /// </summary>
    /// <param name="vault">The vault to add the feature to.</param>
    /// <param name="tenantId">
    /// A settable member with a typed parameter, such as <c>(ITenantEntity x) =&gt; x.TenantId</c>, so the type
    /// arguments are inferred.
    /// </param>
    /// <param name="currentTenantId">Called with the request's services whenever the current tenant is needed.</param>
    public static IVaultBuilder<TVault> UseMultiTenancy<TVault, TTenantEntity, TTenantId>(this IVaultBuilder<TVault> vault,
        Expression<Func<TTenantEntity, TTenantId?>> tenantId,
        Func<IServiceProvider, TTenantId?> currentTenantId)
        where TVault : MongoVault
        where TTenantId : struct =>
        vault.AddFeature(new MultiTenancyFeature<TTenantEntity, TTenantId>(tenantId, currentTenantId));
}

internal sealed class MultiTenancyFeature<TTenantEntity, TTenantId> : IVaultFeature where TTenantId : struct
{
    private readonly Expression<Func<TTenantEntity, TTenantId?>> _tenantId;
    private readonly Func<IServiceProvider, TTenantId?> _currentTenantId;

    public MultiTenancyFeature(Expression<Func<TTenantEntity, TTenantId?>> tenantId,
        Func<IServiceProvider, TTenantId?> currentTenantId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(currentTenantId);

        _tenantId = tenantId;
        _currentTenantId = currentTenantId;
    }

    public FeatureKey Key => MultiTenancyFeature.Key;

    // TODO: Stamp the current tenant on inserts once the save pipeline lets a feature change operations.
    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
        vault.QueryFilter<TTenantEntity>(services => Expression.Lambda<Func<TTenantEntity, bool>>(
            Expression.Equal(_tenantId.Body, Expression.Constant(_currentTenantId(services), typeof(TTenantId?))),
            _tenantId.Parameters));
}

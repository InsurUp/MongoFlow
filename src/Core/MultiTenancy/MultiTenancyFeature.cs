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

/// <summary>Multi-tenancy over a tenant member of type <typeparamref name="TTenant"/>, which may be null.</summary>
internal sealed class MultiTenancyFeature<TTenantEntity, TTenant> : IVaultFeature, IVaultCollectionConfiguration
{
    private readonly Expression<Func<TTenantEntity, TTenant>> _tenantId;
    private readonly Func<TTenantEntity, TTenant> _getTenantId;
    private readonly Action<TTenantEntity, TTenant> _setTenantId;
    private readonly Func<IServiceProvider, TTenant> _currentTenantId;
    private readonly Func<IServiceProvider, bool>? _allTenants;
    private readonly Func<TTenant, bool> _isUnset;

    public MultiTenancyFeature(Expression<Func<TTenantEntity, TTenant>> tenantId,
        Func<IServiceProvider, TTenant> currentTenantId,
        Func<IServiceProvider, bool>? allTenants,
        Func<TTenant, bool> isUnset)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(currentTenantId);

        _tenantId = tenantId;
        _getTenantId = tenantId.Compile();
        _setTenantId = MemberExpressions.CreateSetter(tenantId, nameof(tenantId));
        _currentTenantId = currentTenantId;
        _allTenants = allTenants;
        _isUnset = isUnset;
    }

    public static FeatureKey Key => MultiTenancyFeature.Key;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
        .QueryFilter<TTenantEntity>(Filter)
        .ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(TTenantEntity)))
        {
            collection.AddInterceptor(new MultiTenancyInterceptor<TDocument, TTenantEntity, TTenant>(
                _getTenantId, _setTenantId, _currentTenantId, _allTenants, _isUnset));
        }
    }

    private Expression<Func<TTenantEntity, bool>> Filter(IServiceProvider services)
    {
        if (_allTenants?.Invoke(services) == true)
        {
            return _ => true;
        }

        var current = Expression.Constant(_currentTenantId(services), typeof(TTenant));

        return Expression.Lambda<Func<TTenantEntity, bool>>(Expression.Equal(_tenantId.Body, current), _tenantId.Parameters);
    }
}

/// <summary>
/// Gives inserted and replaced documents without a tenant the current one, and rejects those carrying another tenant, so
/// a write can't move a document out of the current tenant.
/// </summary>
internal sealed class MultiTenancyInterceptor<TDocument, TTenantEntity, TTenant>(
    Func<TTenantEntity, TTenant> getTenantId,
    Action<TTenantEntity, TTenant> setTenantId,
    Func<IServiceProvider, TTenant> currentTenantId,
    Func<IServiceProvider, bool>? allTenants,
    Func<TTenant, bool> isUnset) : VaultInterceptor
{
    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        if (allTenants?.Invoke(context.Services) == true || currentTenantId(context.Services) is not { } current)
        {
            return ValueTask.CompletedTask;
        }

        foreach (var operation in context.Operations)
        {
            if (operation is not (InsertOperation<TDocument> or ReplaceOperation<TDocument>) ||
                operation.Document is not TTenantEntity document)
            {
                continue;
            }

            var tenant = getTenantId(document);
            if (isUnset(tenant))
            {
                setTenantId(document, current);
            }
            else if (!EqualityComparer<TTenant>.Default.Equals(tenant, current))
            {
                throw new InvalidOperationException(
                    $"A {typeof(TDocument).Name} of tenant {tenant} can't be written while the current tenant is {current}.");
            }
        }

        return ValueTask.CompletedTask;
    }
}

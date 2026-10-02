using System.Linq.Expressions;
using System.Reflection;

namespace MongoFlow;

/// <summary>Multi-tenancy over a tenant member of type <typeparamref name="TTenant"/>, which may be null.</summary>
internal sealed class MultiTenancyFeature<TTenantEntity, TTenant> : IVaultFeature, IVaultCollectionConfiguration
{
    private readonly Expression<Func<TTenantEntity, TTenant>> _tenantId;
    private readonly Func<TTenantEntity, TTenant> _getTenantId;
    private readonly Action<TTenantEntity, TTenant> _setTenantId;
    private readonly Func<IServiceProvider, TTenant> _currentTenantId;
    private readonly Func<IServiceProvider, bool>? _allTenants;
    private readonly Func<TTenant, bool> _isUnset;

    // The == the filter compares with, such as string's, looked up once rather than for every query.
    private readonly MethodInfo? _equality;

    public MultiTenancyFeature(Expression<Func<TTenantEntity, TTenant>> tenantId,
        Func<IServiceProvider, TTenant> currentTenantId,
        Func<IServiceProvider, bool>? allTenants,
        Func<TTenant, bool> isUnset)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(currentTenantId);

        _tenantId = tenantId;
        _getTenantId = tenantId.Compile();
        _setTenantId = tenantId.CreateSetter(nameof(tenantId));
        _currentTenantId = currentTenantId;
        _allTenants = allTenants;
        _isUnset = isUnset;
        _equality = Expression.Equal(tenantId.Body, Expression.Default(typeof(TTenant))).Method;
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
                _tenantId, _getTenantId, _setTenantId, _currentTenantId, _allTenants, _isUnset));
            ((CollectionModelBuilder<TDocument>)collection).ExpectIndex(_tenantId);
        }
    }

    private Expression<Func<TTenantEntity, bool>> Filter(IServiceProvider services)
    {
        if (_allTenants?.Invoke(services) == true)
        {
            return _ => true;
        }

        var current = Expression.Constant(_currentTenantId(services), typeof(TTenant));

        return Expression.Lambda<Func<TTenantEntity, bool>>(Expression.Equal(_tenantId.Body, current, liftToNull: false, _equality),
            _tenantId.Parameters);
    }
}

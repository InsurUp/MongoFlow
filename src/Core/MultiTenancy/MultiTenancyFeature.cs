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

internal sealed class MultiTenancyFeature<TTenantEntity, TTenantId> : IVaultFeature, IVaultCollectionConfiguration
    where TTenantId : struct
{
    private readonly Expression<Func<TTenantEntity, TTenantId?>> _tenantId;
    private readonly Func<TTenantEntity, TTenantId?> _getTenantId;
    private readonly Action<TTenantEntity, TTenantId?> _setTenantId;
    private readonly Func<IServiceProvider, TTenantId?> _currentTenantId;

    public MultiTenancyFeature(Expression<Func<TTenantEntity, TTenantId?>> tenantId,
        Func<IServiceProvider, TTenantId?> currentTenantId)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(currentTenantId);

        _tenantId = tenantId;
        _getTenantId = tenantId.Compile();
        _setTenantId = MemberExpressions.CreateSetter(tenantId, nameof(tenantId));
        _currentTenantId = currentTenantId;
    }

    public FeatureKey Key => MultiTenancyFeature.Key;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
        .QueryFilter<TTenantEntity>(services => Expression.Lambda<Func<TTenantEntity, bool>>(
            Expression.Equal(_tenantId.Body, Expression.Constant(_currentTenantId(services), typeof(TTenantId?))),
            _tenantId.Parameters))
        .ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(TTenantEntity)))
        {
            collection.AddInterceptor(
                new MultiTenancyInterceptor<TDocument, TTenantEntity, TTenantId>(_getTenantId, _setTenantId, _currentTenantId));
        }
    }
}

/// <summary>
/// Gives inserted and replaced documents without a tenant the current one, and rejects those carrying another tenant, so
/// a write can't move a document out of the current tenant. With no current tenant, it does neither.
/// </summary>
internal sealed class MultiTenancyInterceptor<TDocument, TTenantEntity, TTenantId>(
    Func<TTenantEntity, TTenantId?> getTenantId,
    Action<TTenantEntity, TTenantId?> setTenantId,
    Func<IServiceProvider, TTenantId?> currentTenantId) : VaultInterceptor
    where TTenantId : struct
{
    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        if (currentTenantId(context.Services) is not { } current)
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

            // An unset tenant is null, or the default value for struct ids that aren't nullable on the document.
            var tenant = getTenantId(document);
            if (tenant is null || EqualityComparer<TTenantId>.Default.Equals(tenant.Value, default))
            {
                setTenantId(document, current);
            }
            else if (!EqualityComparer<TTenantId>.Default.Equals(tenant.Value, current))
            {
                throw new InvalidOperationException(
                    $"A {typeof(TDocument).Name} of tenant {tenant} can't be written while the current tenant is {current}.");
            }
        }

        return ValueTask.CompletedTask;
    }
}

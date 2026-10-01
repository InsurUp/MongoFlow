namespace MongoFlow;

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
            if (operation is not (InsertOperation<TDocument> or ReplaceOperation<TDocument>))
            {
                continue;
            }

            var document = (TTenantEntity)operation.Document!;
            var tenant = getTenantId(document);
            if (isUnset(tenant))
            {
                setTenantId(document, current);
            }
            else if (!EqualityComparer<TTenant>.Default.Equals(tenant, current))
            {
                context.Run.Runtime.Model.Logs.Save.TenantRejected(operation.Namespace.CollectionName, tenant, current);
                throw new InvalidOperationException(
                    $"{typeof(TDocument).Name} of tenant {tenant} can't be written while the current tenant is {current}.");
            }
        }

        return ValueTask.CompletedTask;
    }
}

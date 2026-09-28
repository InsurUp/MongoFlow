namespace MongoFlow;

public class MultiTenancyInterceptor<TInterface, TTenantId> : VaultInterceptor where TTenantId : struct
{
    private readonly VaultMultiTenancyOptions<TInterface, TTenantId> _options;

    public MultiTenancyInterceptor(VaultMultiTenancyOptions<TInterface, TTenantId> options)
    {
        _options = options;
    }

    public override ValueTask SavingChangesAsync(VaultInterceptorContext context, CancellationToken cancellationToken = default)
    {
        var tenantId = _options.TenantIdProvider(context.ServiceProvider);
        if (tenantId is null)
        {
            return ValueTask.CompletedTask;
        }

        var tenantIdSetter = _options.TenantIdSetter;
        var tenantIdGetter = _options.TenantIdGetter;

        foreach (var operation in context.Operations)
        {
            // Both shapes of insert. AddRange carries its documents in CurrentDocuments and leaves
            // CurrentDocument null, so reading only the singular one stamped nothing on a range: the rows
            // went in with no tenant on them, and the query filter then hid them from every read that
            // followed. The row was there, nothing could see it, and nothing had failed.
            foreach (var document in Added(operation))
            {
                if (document is not TInterface entity)
                {
                    continue;
                }

                var documentTenantId = tenantIdGetter(entity);
                if (documentTenantId is null || documentTenantId.Equals(default(TTenantId)))
                {
                    tenantIdSetter(entity, tenantId.Value);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private static IEnumerable<object> Added(VaultOperation operation) => operation switch
    {
        AddRangeOperation range => range.CurrentDocuments,
        { OperationType: OperationType.Add, CurrentDocument: not null } => [operation.CurrentDocument],
        _ => []
    };
}

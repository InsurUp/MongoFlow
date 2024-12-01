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
            if (operation.OperationType is VaultOperationType.Add)
            {
                var documents = operation.CachedDocuments.OfType<TInterface>();
                
                foreach (var document in documents)
                {
                    var documentTenantId = tenantIdGetter(document);
                    if (documentTenantId is null || documentTenantId.Equals(default(TTenantId)))
                    {
                        tenantIdSetter(document, tenantId.Value);
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}

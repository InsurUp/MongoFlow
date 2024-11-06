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
            if (operation.OperationType is OperationType.Add && operation.CurrentDocument is TInterface entity)
            {
                var documentTenantId = tenantIdGetter(entity);
                if (documentTenantId is null || documentTenantId.Equals(default(TTenantId)))
                {
                    tenantIdSetter(entity, tenantId.Value);
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}

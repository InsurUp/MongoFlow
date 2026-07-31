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
            // An AddRange operation carries its batch in CurrentDocuments (its CurrentDocument is null and its
            // OperationType is AddRange), so without unwrapping it the whole batch reaches the database without a
            // tenant id. Single Adds are stamped the same way through their CurrentDocument.
            if (operation is AddRangeOperation addRangeOperation)
            {
                foreach (var document in addRangeOperation.CurrentDocuments)
                {
                    StampIfUnset(document);
                }
            }
            else if (operation.OperationType is OperationType.Add && operation.CurrentDocument is TInterface entity)
            {
                StampIfUnset(entity);
            }
        }

        return ValueTask.CompletedTask;

        void StampIfUnset(object? document)
        {
            if (document is not TInterface tenantEntity)
            {
                return;
            }

            var documentTenantId = tenantIdGetter(tenantEntity);
            if (documentTenantId is null || documentTenantId.Equals(default(TTenantId)))
            {
                tenantIdSetter(tenantEntity, tenantId.Value);
            }
        }
    }
}

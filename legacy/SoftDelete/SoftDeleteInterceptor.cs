namespace MongoFlow;

public class SoftDeleteInterceptor<TSoftDeleteInterface> : VaultInterceptor
{
    private readonly VaultSoftDeleteOptions<TSoftDeleteInterface> _options;

    public SoftDeleteInterceptor(VaultSoftDeleteOptions<TSoftDeleteInterface> options)
    {
        _options = options;
    }

    public override ValueTask SavingChangesAsync(VaultInterceptorContext context,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < context.Operations.Count; i++)
        {
            var operation = context.Operations[i];

            if (operation.OperationType is OperationType.Delete && operation.OldDocument is TSoftDeleteInterface softDeleteDocument)
            {
                _options.ChangeIsDeleted(softDeleteDocument, true);

                var index = context.Operations.IndexOf(operation);

                if (operation.To(OperationType.Update, out var updateOperation) && updateOperation is not null)
                {
                    context.Operations[index] = updateOperation;
                }
                else
                {
                    throw new InvalidOperationException("Failed to convert delete operation to update operation.");
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}

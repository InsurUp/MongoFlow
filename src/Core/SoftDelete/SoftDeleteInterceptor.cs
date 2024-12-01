namespace MongoFlow;

public class SoftDeleteInterceptor<TSoftDeleteInterface> : VaultInterceptor
{
    private readonly VaultSoftDeleteOptions<TSoftDeleteInterface> _options;

    public SoftDeleteInterceptor(VaultSoftDeleteOptions<TSoftDeleteInterface> options)
    {
        _options = options;
    }

    public override async ValueTask SavingChangesAsync(VaultInterceptorContext context,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < context.Operations.Count; i++)
        {
            var operation = context.Operations[i];
            
            if (operation.OperationType is VaultOperationType.Delete &&
                typeof(TSoftDeleteInterface).IsAssignableFrom(operation.DocumentType))
            {
                var documents = await operation.FetchDocumentsAsync(context.Session, cancellationToken);
                if (documents.Length == 0)
                {
                    continue;
                }

                foreach (var document in documents.OfType<TSoftDeleteInterface>())
                {
                    _options.ChangeIsDeleted(document, true);
                }
                
                if (operation.TryConvert(VaultOperationType.Replace, out var replaceOperation))
                {
                    context.Operations[i] = replaceOperation!;
                }
            }
        }
    }
}

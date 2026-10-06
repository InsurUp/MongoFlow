namespace MongoFlow.IntegrationTests;

// An interceptor for every collection that reads through the vault without type arguments.
public partial class UntypedCollectionTests
{
    /// <summary>Reads, before the write, the stored document each write by key targets.</summary>
    public sealed class TargetReader : VaultInterceptor
    {
        public List<object?> Targets { get; } = [];

        public override async ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var operation in context.Operations)
            {
                if (operation is { Kind: not OperationKind.Insert, Key: { } key })
                {
                    var collection = context.Vault.KeyedCollection(operation.Collection.DocumentType);
                    Targets.Add(await collection.WithNoTracking().GetByKeyAsync(key, cancellationToken));
                }
            }
        }
    }
}

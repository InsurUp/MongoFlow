namespace MongoFlow;

/// <summary>
/// A replace or delete matched nothing because the document's concurrency token changed since it was read. The save
/// was rolled back.
/// </summary>
public sealed class ConcurrencyException(VaultOperation operation)
    : Exception($"A {operation.Collection.DocumentType.Name} in {operation.Namespace.CollectionName} was changed after it was read.")
{
    public VaultOperation Operation { get; } = operation;
}

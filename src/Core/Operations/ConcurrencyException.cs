namespace MongoFlow;

/// <summary>
/// A write made with a document matched nothing: the stored document no longer matches what was read, such as its
/// concurrency token, or it's gone. The save was rolled back.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    internal ConcurrencyException(VaultOperation operation, bool documentExists)
        : base(documentExists
            ? $"A {operation.Collection.DocumentType.Name} in {operation.Namespace.CollectionName} was changed after it was read."
            : $"A {operation.Collection.DocumentType.Name} in {operation.Namespace.CollectionName} was deleted, or is no " +
              "longer visible through the collection's query filters, after it was read.")
    {
        Operation = operation;
        DocumentExists = documentExists;
    }

    public VaultOperation Operation { get; }

    /// <summary>
    /// <see langword="true"/> when a document with the key is still stored, but changed; <see langword="false"/> when no
    /// document with its key is visible anymore.
    /// </summary>
    public bool DocumentExists { get; }
}

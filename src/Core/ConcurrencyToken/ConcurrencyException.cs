namespace MongoFlow;

/// <summary>
/// A replace, update or delete made with a document matched nothing, because the stored document's concurrency token no
/// longer has the value that was read, or because the document is gone. The save was rolled back.
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
    /// <see langword="true"/> when the document is still stored with a different token; <see langword="false"/> when no
    /// document with its key is visible anymore.
    /// </summary>
    public bool DocumentExists { get; }
}

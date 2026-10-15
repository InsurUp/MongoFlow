using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A unique index refused a write: a stored document already holds the value it writes, such as an insert repeating a
/// stored key. The save was rolled back.
/// </summary>
/// <remarks>
/// A save is one ordered bulk write, which stops at its first refused write, so there's one operation to name. The
/// driver's <see cref="ClientBulkWriteException"/> is the <see cref="Exception.InnerException"/>, with the server's
/// error, which names the index, and its labels.
/// </remarks>
public sealed class DuplicateKeyException : Exception
{
    internal DuplicateKeyException(VaultOperation operation,
        ClientBulkWriteException innerException)
        : base($"The {operation.Kind.ToString().ToLowerInvariant()} in {operation.Namespace.CollectionName} was refused: " +
               "a unique index already holds the value it writes.",
            innerException)
    {
        Operation = operation;
    }

    /// <summary>The write the unique index refused.</summary>
    public VaultOperation Operation { get; }
}

using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A unique index refused a write: a stored document already holds the value it writes, such as an insert repeating a
/// stored key. The save was rolled back.
/// </summary>
/// <remarks>
/// A save writes in order and stops at its first refused write, so there's one operation to name. The driver's exception
/// is the <see cref="Exception.InnerException"/>, with the server's error, which names the index, and its labels: a
/// <see cref="ClientBulkWriteException"/> from the save's client bulk write, or, from a client that encrypts, which saves
/// with collection commands, a <see cref="MongoWriteException"/> or a <see cref="MongoBulkWriteException"/>.
/// </remarks>
public sealed class DuplicateKeyException : Exception
{
    internal DuplicateKeyException(VaultOperation operation,
        MongoServerException innerException)
        : base($"The {operation.Kind.ToString().ToLowerInvariant()} in {operation.Namespace.CollectionName} was refused: " +
               "a unique index already holds the value it writes.",
            innerException)
    {
        Operation = operation;
    }

    /// <summary>The write the unique index refused.</summary>
    public VaultOperation Operation { get; }
}

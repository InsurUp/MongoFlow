using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A write queued on a vault collection. Nothing is written until the vault is saved.</summary>
/// <remarks>
/// A save sends every queued operation, in order, as one ordered client bulk write inside a transaction. Operations
/// that target a key or a filter also get the collection's query filters, so a write can't reach a document a query
/// couldn't see.
/// </remarks>
public abstract class VaultOperation
{
    private protected VaultOperation(IVaultCollectionInfo collection,
        CollectionNamespace @namespace,
        FeatureSet disabledFeatures)
    {
        Collection = collection;
        Namespace = @namespace;
        DisabledFeatures = disabledFeatures;
    }

    /// <summary>What the operation writes.</summary>
    public abstract OperationKind Kind { get; }

    /// <summary>The vault collection the operation was queued on.</summary>
    public IVaultCollectionInfo Collection { get; }

    /// <summary>The database and collection the operation writes to.</summary>
    public CollectionNamespace Namespace { get; }

    /// <summary>
    /// Insert: the new document. Replace: the replacement. Update or delete made with a document, including a delete
    /// soft delete turned into an update: that document. Otherwise <see langword="null"/>.
    /// </summary>
    public object? Document => GetDocument();

    /// <summary>
    /// The key of the one document the operation targets, of the collection's key type, such as a composite key's
    /// record: read from the document for a write made with one, or the key a write by key was given.
    /// <see langword="null"/> for an insert, whose key is on its <see cref="Document"/>, and for a set-based write.
    /// </summary>
    public abstract object? Key { get; }

    /// <summary>
    /// For a write that brings a tracked document up to date, the document as it was before the save: as it was read,
    /// or as the last save wrote it, serialized with the collection's serializer. These are the update change tracking
    /// writes and a queued <c>Replace</c> or <c>Delete</c> of a tracked document, including the update soft delete
    /// makes of it. <see langword="null"/> for any other write.
    /// </summary>
    /// <remarks>
    /// The save already holds it, to compare the document with, and copies it the first time it's read, so saves no
    /// interceptor reads it in pay nothing. Read it during the save's interceptor hooks; what was read stays readable.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Read for the first time after the save's hooks ran, or after the vault was disposed.
    /// </exception>
    public RawBsonDocument? Original => TrackedOriginal?.Read();

    /// <summary>
    /// <see langword="true"/> for operations that target a filter rather than a key, which can change many documents.
    /// </summary>
    public abstract bool IsSetBased { get; }

    /// <summary>
    /// The filter a set-based write matches, rendered with the collection's serializers as the driver renders the
    /// write, so captured values show as values. <see langword="null"/> for any other write: one that targets a
    /// document has its <see cref="Key"/>. The query filters and conditions added to it when it's sent aren't part of
    /// it.
    /// </summary>
    /// <remarks>Rendered each time it's called.</remarks>
    public virtual BsonDocument? RenderFilter() => null;

    /// <summary>
    /// An update's definition, rendered the same way: a document of update operators, or an array for a pipeline. It's
    /// rendered as it stands when called; built-in features add to it during
    /// <see cref="VaultInterceptor.SavingAsync"/>, such as the concurrency token's increment.
    /// <see langword="null"/> for an insert, a replace and a delete.
    /// </summary>
    /// <remarks>Rendered each time it's called.</remarks>
    public virtual BsonValue? RenderUpdate() => null;

    /// <summary>What the operation changed. Available after the bulk write.</summary>
    public OperationResult? Result { get; internal set; }

    /// <summary>The features switched off on the collection view the operation was queued through.</summary>
    internal FeatureSet DisabledFeatures { get; }

    /// <summary>Where <see cref="Original"/> is, for a write of a tracked document.</summary>
    internal TrackedOriginal? TrackedOriginal { get; set; }

    /// <summary>The model this operation adds to the save's bulk write.</summary>
    internal abstract ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken);

    private protected abstract object? GetDocument();
}

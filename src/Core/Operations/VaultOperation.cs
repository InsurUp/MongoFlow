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

    public abstract OperationKind Kind { get; }

    public IVaultCollectionInfo Collection { get; }

    /// <summary>The database and collection the operation writes to.</summary>
    public CollectionNamespace Namespace { get; }

    /// <summary>
    /// Insert: the new document. Replace: the replacement. Update or delete made with a document, including a delete
    /// soft delete turned into an update: that document. Otherwise <see langword="null"/>.
    /// </summary>
    public object? Document => GetDocument();

    /// <summary>
    /// <see langword="true"/> for operations that target a filter rather than a key, which can change many documents.
    /// </summary>
    public abstract bool IsSetBased { get; }

    /// <summary>What the operation changed. Available after the bulk write.</summary>
    public OperationResult? Result { get; internal set; }

    /// <summary>The features switched off on the collection view the operation was queued through.</summary>
    internal FeatureSet DisabledFeatures { get; }

    /// <summary>The key of the one document the operation targets, or <see langword="null"/> for inserts and set-based operations.</summary>
    internal abstract object? TargetKey { get; }

    /// <summary>The model this operation adds to the save's bulk write.</summary>
    internal abstract ValueTask<BulkWriteModel> CreateWriteModelAsync(SaveRun run,
        CancellationToken cancellationToken);

    private protected abstract object? GetDocument();
}

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
        IReadOnlySet<FeatureKey> disabledFeatures)
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
    /// Insert: the new document. Replace: the replacement. Delete made with a document: that document. Otherwise
    /// <see langword="null"/>; an update carries a definition, not a document.
    /// </summary>
    public object? Document => GetDocument();

    /// <summary>
    /// The stored document before the change. Read, in one query per collection, only when an interceptor declared it
    /// needs originals for this collection and kind. Always <see langword="null"/> for inserts and set-based operations.
    /// </summary>
    public object? Original => GetOriginal();

    /// <summary>
    /// <see langword="true"/> for operations that target a filter rather than a key, which can change many documents.
    /// </summary>
    public abstract bool IsSetBased { get; }

    /// <summary>What the operation changed. Available after the bulk write.</summary>
    public OperationResult? Result { get; internal set; }

    /// <summary>The features switched off on the collection view the operation was queued through.</summary>
    internal IReadOnlySet<FeatureKey> DisabledFeatures { get; }

    private protected abstract object? GetDocument();

    private protected abstract object? GetOriginal();
}

public abstract class VaultOperation<TDocument> : VaultOperation
{
    private protected VaultOperation(IVaultCollectionInfo collection,
        CollectionNamespace @namespace,
        IReadOnlySet<FeatureKey> disabledFeatures)
        : base(collection, @namespace, disabledFeatures)
    {
    }

    /// <inheritdoc cref="VaultOperation.Document"/>
    public new TDocument? Document { get; private protected init; }

    /// <inheritdoc cref="VaultOperation.Original"/>
    public new TDocument? Original { get; internal set; }

    private protected override object? GetDocument() => Document;

    private protected override object? GetOriginal() => Original;
}

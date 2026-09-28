using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Applies an update definition to the document with a key, or to every document matching a filter.</summary>
public sealed class UpdateOperation<TDocument> : VaultOperation<TDocument>
{
    internal UpdateOperation(IVaultCollectionInfo collection,
        CollectionNamespace @namespace,
        IReadOnlySet<FeatureKey> disabledFeatures,
        object? key,
        Expression<Func<TDocument, bool>>? filter,
        UpdateDefinition<TDocument> update)
        : base(collection, @namespace, disabledFeatures)
    {
        if ((key is null) == (filter is null))
        {
            throw new ArgumentException("An update targets either a key or a filter.");
        }

        Key = key;
        Filter = filter;
        Update = update;
    }

    public override OperationKind Kind => OperationKind.Update;

    public override bool IsSetBased => Filter is not null;

    /// <summary>The key of the one document to update, or <see langword="null"/> when the operation is set-based.</summary>
    public object? Key { get; }

    /// <summary>The documents to update, or <see langword="null"/> when the operation targets a key.</summary>
    public Expression<Func<TDocument, bool>>? Filter { get; }

    public UpdateDefinition<TDocument> Update { get; }
}

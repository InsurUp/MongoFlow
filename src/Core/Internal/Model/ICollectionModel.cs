namespace MongoFlow;

/// <summary>
/// What code holding a vault's collections of different document types together needs: the migrator ensuring the
/// schema, and lookup by document type. Everything else goes through the typed <see cref="CollectionModel{TDocument}"/>.
/// </summary>
internal interface ICollectionModel : IVaultCollectionInfo
{
    Task EnsureCreatedAsync(ISet<string> existing, CancellationToken cancellationToken);

    Task EnsureIndexesAsync(CancellationToken cancellationToken);
}
    
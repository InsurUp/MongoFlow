using System.Reflection;

namespace MongoFlow;

/// <summary>
/// What code holding a vault's collections of different document types together needs. Everything else goes through
/// the typed <see cref="CollectionModel{TDocument}"/>.
/// </summary>
internal interface ICollectionModel : IVaultCollectionInfo
{
    /// <summary>The vault property the collection is declared as.</summary>
    PropertyInfo Property { get; }

    /// <summary>Fills <see cref="Property"/> on <paramref name="vault"/> with a collection bound to <paramref name="runtime"/>.</summary>
    void Attach(MongoVault vault, VaultRuntime runtime);
}

using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// What code holding a vault's collections of different document types together needs. Everything else goes through
/// the typed <see cref="CollectionModel{TDocument}"/>.
/// </summary>
internal interface ICollectionModel : IVaultCollectionInfo
{
    /// <summary>The vault property the collection is declared as.</summary>
    PropertyInfo Property { get; }

    CollectionNamespace Namespace { get; }

    /// <summary>The driver's collection as a constant, for a join to look documents up in.</summary>
    ConstantExpression MongoCollectionConstant { get; }

    /// <summary>The element names of the key, or <see langword="null"/> for a keyless collection.</summary>
    IReadOnlyList<string>? KeyFields { get; }

    /// <summary>The fields features filter every read on, rendered with the collection's serializers.</summary>
    IEnumerable<IndexedField> RenderIndexedFields();

    /// <summary>Fills <see cref="Property"/> on <paramref name="vault"/> with a collection bound to <paramref name="runtime"/>.</summary>
    void Attach(MongoVault vault, VaultRuntime runtime);

    /// <summary>
    /// The collection bound to <paramref name="runtime"/>, without its type arguments: an
    /// <see cref="IKeyedVaultCollection"/> if it's keyed.
    /// </summary>
    IVaultCollection CreateCollection(VaultRuntime runtime);
}

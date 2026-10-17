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

    /// <summary>How the collection's encrypted members are written and queried, or <see langword="null"/> when it has none.</summary>
    ICollectionEncryption? Encryption { get; }

    /// <summary>
    /// Whether <paramref name="filter"/>, a filter of the collection's documents, compares one of its encrypted members,
    /// which only a filter MongoFlow encrypts can.
    /// </summary>
    bool Touches(LambdaExpression filter);

    /// <summary>The search indexes the collection declares, which the migrator creates and keeps up to date.</summary>
    IReadOnlyList<SearchIndexDeclaration> SearchIndexes { get; }

    /// <summary>The indexes the collection declares, which the migrator creates after the vault's migrations.</summary>
    IReadOnlyList<IndexDeclaration> Indexes { get; }

    /// <summary>
    /// What the collection declares it's created with, which the migrator creates it with before the vault's migrations, or
    /// <see langword="null"/> when it declares nothing.
    /// </summary>
    CollectionSettings? Settings { get; }

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

using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Search;

/// <summary>
/// A collection's <c>SearchIndex</c> and <c>VectorSearchIndex</c> declare the indexes the migrator sets up:
/// <list type="number">
/// <item>members are mapped by their element names, text with its alternates, dotted paths as document mappings following
/// the index's dynamic setting, or the one declared for documents, but for documents mapped already, on top of a
/// definition given as it is;</item>
/// <item>the fields features filter on are mapped as their values compare, as filter fields of a vector index, unless
/// they're mapped so already or can't be compared exactly;</item>
/// <item>an index declared again is configured further, a type mapped again replaces the earlier mapping, and a vector or
/// filter field declared again replaces the earlier one;</item>
/// <item>a name used by both kinds, a vector index without a vector, and any index on a collection with encrypted members
/// fail the model, and a vault declaring an index gets the migrator's setup for it.</item>
/// </list>
/// </summary>
public partial class SearchIndexBuilderTests
{
    [Test]
    public async Task SearchIndex_Declared_MapsItsMembersAndTheFeaturesFields()
    {
        // Arrange
        await using var host = Host(products => products.SearchIndex("default", index => index
            .Analyzer("lucene.turkish")
            .SearchAnalyzer("lucene.standard")
            .Map(x => x.Name, SearchField.String(), SearchField.Autocomplete(), SearchField.Token())
            .Map(x => x.Supplier!.City, SearchField.Token())
            .Map("Supplier.Country.Code", SearchField.Token())));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task SearchIndex_TextWithAnAlternate_IsMappedWithItBesideTheFieldsOtherMappings()
    {
        // Arrange
        await using var host = Host(products => products.SearchIndex("default", index => index
            .Analyzer("lucene.turkish")
            .Map(x => x.Name,
                SearchField.String("lucene.turkish").Multi("keyword", SearchField.String("lucene.keyword")),
                SearchField.Autocomplete(2, 20),
                SearchField.Token())));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task SearchIndex_DocumentsStaticInADynamicIndex_CreatesEachDocumentStatic()
    {
        // Arrange
        await using var host = Host(products => products.SearchIndex("default", index => index
            .Dynamic()
            .DocumentsDynamic(false)
            .Map(x => x.Supplier!.City, SearchField.Token())
            .Map("Supplier.Country.Code", SearchField.Token())));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task SearchIndex_DocumentMappedBefore_KeepsItsOwnDynamic()
    {
        // Arrange — Supplier mapped in the definition, Archive as a raw mapping; Country is created.
        await using var host = Host(products => products.SearchIndex("default", index => index
            .Definition(BsonDocument.Parse("{ mappings: { fields: { Supplier: { type: 'document', dynamic: true } } } }"))
            .Dynamic()
            .DocumentsDynamic(false)
            .Map("Archive", SearchField.Raw(BsonDocument.Parse("{ type: 'document', dynamic: true }")))
            .Map(x => x.Supplier!.City, SearchField.Token())
            .Map("Supplier.Country.Code", SearchField.Token())
            .Map("Archive.Note", SearchField.String())));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task SearchIndex_FeatureFieldMappedAlready_IsLeftAsMapped()
    {
        // Arrange
        await using var host = Host(products => products.SearchIndex("default", index => index
            .Map(x => x.TenantId, SearchField.Token(lowercase: true))
            .Map(x => x.DeletedAt, SearchField.Date())));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task SearchIndex_DynamicOnADefinition_AddsWhatsDeclared()
    {
        // Arrange
        await using var host = Host(products => products.SearchIndex("default", index => index
            .Definition(BsonDocument.Parse("{ storedSource: true, mappings: { fields: { Name: { type: 'string' } } } }"))
            .Dynamic()
            .Map("Supplier.city", SearchField.Token())));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task SearchIndex_DeclaredAgain_IsConfiguredFurther()
    {
        // Arrange
        await using var host = Host(products => products
            .SearchIndex("default", index => index.Map(x => x.Name, SearchField.String(), SearchField.Autocomplete()))
            .SearchIndex("default", index => index.Map(x => x.Name, SearchField.Autocomplete(maxGrams: 7)).Dynamic(false)));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task VectorSearchIndex_Declared_IndexesItsVectorsAndTheFeaturesFields()
    {
        // Arrange
        await using var host = Host(products => products.VectorSearchIndex("vectors", index => index
            .Definition(BsonDocument.Parse("{ fields: [{ type: 'vector', path: 'Embedding', numDimensions: 2, similarity: 'cosine' }] }"))
            .Vector(x => x.Embedding, dimensions: 1536, VectorSimilarity.DotProduct, VectorQuantization.Scalar)
            .Vector(x => x.Supplier, dimensions: 3, VectorSimilarity.Euclidean, VectorQuantization.Binary)
            .Filter(x => x.Price)
            .Filter("Supplier.city")
            .Filter(x => x.TenantId)));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations);
    }

    [Test]
    [Arguments(VectorQuantization.None)]
    [Arguments(VectorQuantization.BinaryNoRescore)]
    public async Task VectorSearchIndex_Quantization_IsWrittenAsTheServerTakesIt(VectorQuantization quantization)
    {
        // Arrange
        await using var host = Host(products => products.VectorSearchIndex("vectors", index => index
            .Vector(x => x.Embedding, dimensions: 8, VectorSimilarity.Cosine, quantization)));

        // Act
        var declarations = Declarations(host);

        // Assert
        await Verify(declarations).UseParameters(quantization);
    }

    [Test]
    public async Task SearchIndex_FeatureFieldOfADocument_IsLeftUnmapped()
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>(vault => vault
            .UseMultiTenancy((Shipment x) => x.Region, _ => new Region("eu"))
            .Collection(x => x.Shipments, shipments => shipments
                .SearchIndex("default", index => index.Dynamic())
                .VectorSearchIndex("vectors", index => index.Vector(x => x.Id, dimensions: 2, VectorSimilarity.Cosine))));

        // Act
        var declarations = Rendered(Model(host).Collections.Single(collection => collection.DocumentType == typeof(Shipment)));

        // Assert
        await Verify(declarations);
    }

    [Test]
    public async Task SearchIndex_NameOfAVectorSearchIndex_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = Host(products => products
            .SearchIndex("default", index => index.Dynamic())
            .VectorSearchIndex("default", index => index.Vector(x => x.Embedding, dimensions: 3, VectorSimilarity.Cosine)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task VectorSearchIndex_WithoutAVector_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = Host(products => products.VectorSearchIndex("vectors", index => index.Filter(x => x.Price)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    [Arguments(0)]
    [Arguments(8193)]
    public async Task Vector_DimensionsOutsideWhatMongoDbTakes_ThrowsArgumentOutOfRangeException(int dimensions)
    {
        // Arrange
        await using var host = Host(products => products.VectorSearchIndex("vectors", index => index
            .Vector(x => x.Embedding, dimensions, VectorSimilarity.Cosine)));

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments("no fields")]
    [Arguments("a null field")]
    [Arguments("a blank path")]
    [Arguments("a blank name")]
    [Arguments("a blank analyzer")]
    public async Task SearchIndex_ArgumentMongoDbCantTake_ThrowsArgumentException(string argument)
    {
        // Arrange
        await using var host = Host(products => _ = argument switch
        {
            "no fields" => products.SearchIndex("default", index => index.Map(x => x.Name)),
            "a null field" => products.SearchIndex("default", index => index.Map(x => x.Name, null!)),
            "a blank path" => products.SearchIndex("default", index => index.Map(" ", SearchField.Token())),
            "a blank name" => products.SearchIndex(" ", index => index.Dynamic()),
            _ => products.SearchIndex("default", index => index.Analyzer(""))
        });

        // Act & Assert
        await Assert.That(() => host.Vault).Throws<ArgumentException>();
    }

    [Test]
    public async Task Build_NoIndexDeclared_LeavesTheMigratorNothingToSetUp()
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>();

        // Act
        var setups = Model(host).Setups;

        // Assert
        await Assert.That(setups).IsEmpty();
    }

    [Test]
    public async Task Build_IndexDeclared_HasTheMigratorSetItUp()
    {
        // Arrange
        await using var host = Host(products => products.SearchIndex("default", index => index.Dynamic()));

        // Act
        var setups = Model(host).Setups;

        // Assert
        await Assert.That(setups.Single()).IsTypeOf<SearchIndexSetup>();
    }

    private static VaultHost<CatalogVault> Host(Action<IVaultCollectionBuilder<Product, ObjectId>> products) =>
        new(vault => vault
            .UseMultiTenancy((ITenantOwned x) => x.TenantId, _ => "t-1")
            .UseSoftDelete((Product x) => x.DeletedAt)
            .Collection(x => x.Products, products));

    private static VaultModel Model(VaultHost<CatalogVault> host) =>
        host.Services.GetRequiredService<VaultModelProvider<CatalogVault>>().Model;

    private static IEnumerable<object> Declarations(VaultHost<CatalogVault> host) =>
        Rendered(Model(host).Collections.Single(collection => collection.DocumentType == typeof(Product)));

    private static IEnumerable<object> Rendered(ICollectionModel collection) =>
        collection.SearchIndexes.Select(index => new { index.Name, index.TypeName, index.Definition });
}

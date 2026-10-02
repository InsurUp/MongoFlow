using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="IVaultCollection{TDocument}"/> and <see cref="IVaultCollection{TDocument,TKey}"/>, before anything reaches
/// the server:
/// <list type="number">
/// <item>reads start from the collection's query filters, joined with the read's own;</item>
/// <item>a query joined with another collection's query looks up through that query's filters, and only filters;</item>
/// <item>a view with a feature switched off stays keyed when the collection is;</item>
/// <item>null arguments, and documents whose key is null, are rejected when the write is queued.</item>
/// </list>
/// </summary>
public partial class VaultCollectionTests
{
    [Test]
    public async Task MongoCollection_OfAVaultCollection_IsTheDriversCollection()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var collection = host.Vault.Orders.MongoCollection;

        // Assert
        await Assert.That(collection.CollectionNamespace.FullName).IsEqualTo("unit.Orders");
    }

    [Test]
    public async Task FindAsync_WithQueryFilters_FindsWhatBothMatch()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.QueryFilter<ISoftDeletable>(x => !x.IsDeleted));

        // Act
        var find = await host.Vault.Orders.FindAsync(x => x.Customer == "ada");

        // Assert
        await Verify(find.ToString());
    }

    [Test]
    public async Task FindAsync_FilterDefinitionWithQueryFilters_FindsWhatBothMatch()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.QueryFilter<ISoftDeletable>(x => !x.IsDeleted));

        // Act
        var find = await host.Vault.Orders.FindAsync(Builders<Order>.Filter.Eq(x => x.Customer, "ada"));

        // Assert
        await Verify(find.ToString());
    }

    [Test]
    public async Task FindAsync_FilterDefinitionWithoutQueryFilters_FindsWhatItMatches()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var find = await host.Vault.Orders.FindAsync(Builders<Order>.Filter.Gt(x => x.Total, 10));

        // Assert
        await Verify(find.ToString());
    }

    [Test]
    public async Task FindAsync_ConstantTrueWithoutQueryFilters_FindsEverything()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var find = await host.Vault.Orders.FindAsync(_ => true);

        // Assert
        await Verify(find.ToString());
    }

    [Test]
    public async Task FindAsync_OnAKeylessCollection_FindsWhatItMatches()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var find = await host.Vault.Audit.FindAsync(x => x.Message == "saved");

        // Assert
        await Verify(find.ToString());
    }

    [Test]
    public async Task AggregateAsync_WithQueryFilters_StartsWithTheirMatch()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.QueryFilter<ISoftDeletable>(x => !x.IsDeleted));

        // Act
        var aggregate = await host.Vault.Orders.AggregateAsync();

        // Assert
        await Verify(aggregate.ToString());
    }

    [Test]
    public async Task AggregateAsync_WithoutQueryFilters_HasNoStages()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var aggregate = await host.Vault.Orders.AggregateAsync();

        // Assert
        await Verify(aggregate.ToString());
    }

    [Test]
    public async Task Without_OnAKeylessCollection_SwitchesTheFeatureOffForItsReads()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.AddFeature(new TenantOneFeature()));

        // Act
        var queries = new
        {
            On = (await host.Vault.Audit.QueryAsync()).ToString(),
            Off = (await host.Vault.Audit.Without(TenantOneFeature.Key).QueryAsync()).ToString()
        };

        // Assert
        await Verify(queries);
    }

    [Test]
    public async Task Without_OnAKeyedCollection_ReturnsAKeyedView()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        IVaultCollection<Order> orders = host.Vault.Orders;

        // Act
        var view = orders.Without(new FeatureKey("audit"));

        // Assert
        await Assert.That(view).IsAssignableTo<IVaultCollection<Order, int>>();
    }

    [Test]
    public async Task Without_OnAKeyedView_ReturnsAKeyedView()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var view = host.Vault.Orders.Without(new FeatureKey("audit")).Without(new FeatureKey("tenancy"));

        // Assert
        await Assert.That(view.MongoCollection).IsSameReferenceAs(host.Vault.Orders.MongoCollection);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Without_DefaultKey_ThrowsArgumentException(bool keyed)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        IVaultCollection<Order> keyless = host.Vault.Orders;

        // Act & Assert
        await Assert.That(() => keyed ? host.Vault.Orders.Without(default) : keyless.Without(default))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    [MethodDataSource(nameof(NullArguments))]
    public async Task Write_NullArgument_ThrowsArgumentNullException(string call,
        Action<CatalogVault> write)
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>();

        // Act & Assert
        await Assert.That(() => write(host.Vault)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task FindAsync_NullFilter_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>();

        // Act & Assert
        await Assert.That(async () => await host.Vault.Products.FindAsync((Expression<Func<Product, bool>>)null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task FindAsync_NullFilterDefinition_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>();

        // Act & Assert
        await Assert.That(async () => await host.Vault.Products.FindAsync((FilterDefinition<Product>)null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task GetByKeyAsync_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>();

        // Act & Assert
        await Assert.That(async () => await host.Vault.Products.GetByKeyAsync(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Replace_DocumentWithoutAKey_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>();

        // Act & Assert
        await Throws(() => host.Vault.Products.Replace(new Product())).IgnoreStackTrace();
    }

    [Test]
    [MethodDataSource(nameof(KeylessDocumentWrites))]
    public async Task Write_DocumentWithoutAKey_ThrowsArgumentException(string call,
        Action<CatalogVault> write)
    {
        // Arrange
        await using var host = new VaultHost<CatalogVault>();

        // Act & Assert
        await Assert.That(() => write(host.Vault)).ThrowsExactly<ArgumentException>();
    }

    public static IEnumerable<Func<(string, Action<CatalogVault>)>> NullArguments()
    {
        var update = Builders<Product>.Update.Set(x => x.Name, "renamed");

        yield return () => ("Add", vault => vault.Notes.Add(null!));
        yield return () => ("AddRange", vault => vault.Notes.AddRange(null!));
        yield return () => ("UpdateMany(filter)", vault => vault.Products.UpdateMany(null!, update));
        yield return () => ("UpdateMany(update)", vault => vault.Products.UpdateMany(x => x.Name == "", null!));
        yield return () => ("DeleteMany", vault => vault.Notes.DeleteMany(null!));
        yield return () => ("Replace", vault => vault.Products.Replace(null!));
        yield return () => ("Delete", vault => vault.Products.Delete(null!));
        yield return () => ("DeleteByKey", vault => vault.Products.DeleteByKey(null!));
        yield return () => ("UpdateByKey(key)", vault => vault.Products.UpdateByKey(null!, update));
        yield return () => ("UpdateByKey(update)", vault => vault.Products.UpdateByKey("p-1", null!));
        yield return () => ("Update(document)", vault => vault.Products.Update(null!, update));
        yield return () => ("Update(update)", vault => vault.Products.Update(new Product { Id = "p-1" }, null!));
    }

    public static IEnumerable<Func<(string, Action<CatalogVault>)>> KeylessDocumentWrites()
    {
        yield return () => ("Delete", vault => vault.Products.Delete(new Product()));
        yield return () => ("Update", vault => vault.Products.Update(new Product(), Builders<Product>.Update.Set(x => x.Name, "renamed")));
    }
}

using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="IVaultBuilder{TVault}"/> and the collection builders, as a vault's model is built from them:
/// <list type="number">
/// <item>the vault's collections are its public collection properties, which need a setter and distinct documents;</item>
/// <item>collections are selected by property, also through an interface the vault implements;</item>
/// <item>defaults, the vault's own <c>Configure</c> and the registration apply in that order: a single value is the
/// last own one, else the default's, and lists accumulate;</item>
/// <item>default configurations apply to every vault whose type fits, unless the vault skips them;</item>
/// <item>query filters apply to the documents they target, and those a feature adds switch off with it;</item>
/// <item>invalid arguments and configurations fail when the model is built, before first use.</item>
/// </list>
/// </summary>
public partial class VaultBuilderTests
{
    [Test]
    public async Task Collections_DeclaredOnTheVault_ListsOnlyThem()
    {
        // Arrange
        IReadOnlyList<IVaultCollectionInfo>? collections = null;
        await using var host = new VaultHost<MixedVault>(vault => collections = vault.Collections);

        // Act
        _ = host.Vault;

        // Assert
        await Verify(collections!.Select(collection => new { collection.PropertyName, collection.DocumentType, collection.KeyType }));
    }

    [Test]
    public async Task Build_CollectionWithoutASetter_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<NoSetterVault>();

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_TwoCollectionsOfOneDocument_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<DuplicateVault>();

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_KeylessDocumentThatReadsNoId_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<StrictVault>();

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_KeyedByAnotherMemberAndReadingNoId_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<NumberedVault>(vault => vault
            .Collection(x => x.Notes, notes => notes.Key(x => x.Number)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_KeylessCollectionOfBsonDocuments_RegistersNoClassMapForThem()
    {
        // Arrange
        await using var host = new VaultHost<RawVault>();

        // Act
        _ = host.Vault;

        // Assert
        await Assert.That(BsonClassMap.IsClassMapRegistered(typeof(BsonDocument))).IsFalse();
    }

    [Test]
    public async Task Build_KeylessDocumentWithAnIdOrOfAnInterface_IsAccepted()
    {
        // Arrange
        await using var host = new VaultHost<LooseVault>();

        // Act
        var vault = host.Vault;

        // Assert
        await Verify(new
        {
            Orders = vault.Orders.MongoCollection.CollectionNamespace.CollectionName,
            Events = vault.Events.MongoCollection.CollectionNamespace.CollectionName
        });
    }

    [Test]
    public async Task Build_NoDatabase_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var services = new ServiceCollection().AddMongoVault<ShopVault>().BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // Act & Assert
        await Throws(() => scope.ServiceProvider.GetRequiredService<ShopVault>()).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_SecondResolve_ReusesTheModel()
    {
        // Arrange
        var counter = new ApplyCounter();
        await using var host = new VaultHost<ShopVault>(_ => counter.Count++);
        await using var other = host.Services.CreateAsyncScope();

        // Act
        _ = host.Vault;
        _ = other.ServiceProvider.GetRequiredService<ShopVault>();

        // Assert
        await Assert.That(counter.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Name_NotSet_IsThePropertyName()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(nameof(ShopVault.Orders));
    }

    [Test]
    public async Task Name_Set_NamesTheCollection()
    {
        // Arrange
        const string Name = "audit_entries";
        await using var host = new VaultHost<ShopVault>(vault => vault.Collection(x => x.Audit, audit => audit.Name(Name)));

        // Act
        var name = host.Vault.Audit.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(Name);
    }

    [Test]
    public async Task Collection_KeyedPropertyThroughTheKeylessOverload_ConfiguresIt()
    {
        // Arrange
        const string Name = "all_orders";
        await using var host = new VaultHost<ShopVault>(vault =>
            vault.Collection<Order>(x => x.Orders, orders => orders.Name(Name)));

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(Name);
    }

    [Test]
    public async Task Collection_PropertyOfAnInterfaceTheVaultImplements_ConfiguresTheVaultsOwn()
    {
        // Arrange
        await using var host = new VaultHost<AuditedVault>(services: services =>
            services.AddDefaultVaultConfiguration(typeof(AuditNaming<>)));

        // Act
        var name = host.Vault.Audit.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(AuditNaming<AuditedVault>.Name);
    }

    [Test]
    public async Task Collection_Field_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<MixedVault>(vault => vault.Collection(x => x.AuditField!, _ => { }));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Collection_NotAMember_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.Collection(x => x.Orders.Without(ActiveOnlyFeature.Key), _ => { }));

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Collection_PropertyThatIsNotDeclared_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<MixedVault>(vault => vault.Collection(x => x.Hidden, _ => { }));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Collection_KeylessPropertyAsKeyed_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault =>
            vault.Collection(x => (IVaultCollection<AuditEntry, int>)x.Audit, _ => { }));

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ForEachCollection_Configuration_ConfiguresEveryCollection()
    {
        // Arrange
        const string Prefix = "shop_";
        await using var host = new VaultHost<ShopVault>(vault => vault.ForEachCollection(new Prefix(Prefix)));

        // Act
        var vault = host.Vault;

        // Assert
        await Verify(new
        {
            Orders = vault.Orders.MongoCollection.CollectionNamespace.CollectionName,
            Audit = vault.Audit.MongoCollection.CollectionNamespace.CollectionName
        });
    }
}

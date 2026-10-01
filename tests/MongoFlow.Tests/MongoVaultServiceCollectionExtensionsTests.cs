using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="MongoVaultServiceCollectionExtensions.AddMongoVault{TVault}(IServiceCollection, Action{IVaultBuilder{TVault}})"/>
/// and its overloads register a vault per scope:
/// <list type="number">
/// <item>registering a vault again adds to its configuration;</item>
/// <item>the provider overloads hand the configuration the root provider;</item>
/// <item>an app interface resolves to the scope's vault;</item>
/// <item>a vault gets its constructor's dependencies from the scope, and shares the scope's transaction manager.</item>
/// </list>
/// </summary>
public partial class MongoVaultServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddMongoVault_NullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => MongoVaultServiceCollectionExtensions.AddMongoVault<ShopVault>(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task AddMongoVault_NullServicesWithTheProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => MongoVaultServiceCollectionExtensions.AddMongoVault<ShopVault>(null!, (_, _) => { }))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task AddMongoVault_NullConfigurationWithTheProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new ServiceCollection().AddMongoVault((Action<IServiceProvider, IVaultBuilder<ShopVault>>)null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task AddMongoVault_Twice_AppliesBothConfigurations()
    {
        // Arrange
        const string OrdersName = "first";
        const string AuditName = "second";
        await using var services = Services()
            .AddMongoVault<ShopVault>(vault => vault.UseDatabase(Offline.DatabaseName))
            .AddMongoVault<ShopVault>(vault => vault.Collection(x => x.Orders, orders => orders.Name(OrdersName)))
            .AddMongoVault<ShopVault>((_, vault) => vault.Collection(x => x.Audit, audit => audit.Name(AuditName)))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // Act
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();

        // Assert
        await Verify(new
        {
            Orders = vault.Orders.MongoCollection.CollectionNamespace.CollectionName,
            Audit = vault.Audit.MongoCollection.CollectionNamespace.CollectionName
        });
    }

    [Test]
    public async Task AddMongoVault_WithTheProvider_PassesTheRootProvider()
    {
        // Arrange
        const string Name = "from-provider";
        await using var services = Services()
            .AddMongoVault<ShopVault>((provider, vault) => vault.UseDatabase(provider.GetRequiredService<IMongoClient>().GetDatabase(Name)))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // Act
        var vault = scope.ServiceProvider.GetRequiredService<ShopVault>();

        // Assert
        await Assert.That(vault.Orders.MongoCollection.Database.DatabaseNamespace.DatabaseName).IsEqualTo(Name);
    }

    [Test]
    public async Task AddMongoVault_WithAnInterface_ResolvesItToTheScopesVault()
    {
        // Arrange
        await using var services = Services()
            .AddMongoVault<IShopVault, InterfacedVault>(vault => vault.UseDatabase(Offline.DatabaseName))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // Act
        var vault = scope.ServiceProvider.GetRequiredService<IShopVault>();

        // Assert
        await Assert.That(vault).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<InterfacedVault>());
    }

    [Test]
    public async Task AddMongoVault_WithAnInterfaceAndTheProvider_ResolvesItToTheScopesVault()
    {
        // Arrange
        await using var services = Services()
            .AddMongoVault<IShopVault, InterfacedVault>((_, vault) => vault.UseDatabase(Offline.DatabaseName))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        // Act
        var vault = scope.ServiceProvider.GetRequiredService<IShopVault>();

        // Assert
        await Assert.That(vault).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<InterfacedVault>());
    }

    [Test]
    public async Task AddMongoVault_Vault_IsOnePerScope()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        await using var other = host.Services.CreateAsyncScope();

        // Act
        var first = host.Vault;

        // Assert
        await Assert.That(host.Vault).IsSameReferenceAs(first);
        await Assert.That(other.ServiceProvider.GetRequiredService<ShopVault>()).IsNotSameReferenceAs(first);
    }

    [Test]
    public async Task AddMongoVault_VaultWithDependencies_GetsThemFromTheScope()
    {
        // Arrange
        await using var host = new VaultHost<DependentVault>(services: services => services.AddScoped<CurrentCustomer>());

        // Act
        var customer = host.Vault.Customer;

        // Assert
        await Assert.That(customer).IsSameReferenceAs(host.Services.GetRequiredService<CurrentCustomer>());
    }

    [Test]
    public async Task AddMongoVault_TransactionManager_IsOnePerScope()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        await using var other = host.Services.CreateAsyncScope();

        // Act
        var first = host.Services.GetRequiredService<IVaultTransactionManager>();

        // Assert
        await Assert.That(host.Services.GetRequiredService<IVaultTransactionManager>()).IsSameReferenceAs(first);
        await Assert.That(other.ServiceProvider.GetRequiredService<IVaultTransactionManager>()).IsNotSameReferenceAs(first);
    }

    private static IServiceCollection Services() => new ServiceCollection().AddSingleton(Offline.Client);
}

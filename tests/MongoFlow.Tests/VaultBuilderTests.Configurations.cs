using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

public partial class VaultBuilderTests
{
    [Test]
    public async Task UseConfiguration_Type_IsCreatedFromTheRootProvider()
    {
        // Arrange
        const string Prefix = "options_";
        await using var host = new VaultHost<ShopVault>(
            vault => vault.UseConfiguration<NamingConfiguration>(),
            services => services.AddSingleton(new NamingOptions(Prefix)));

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(Prefix + nameof(ShopVault.Orders));
    }

    [Test]
    public async Task UseConfiguration_Instance_IsApplied()
    {
        // Arrange
        const string Name = "instance_orders";
        await using var host = new VaultHost<ShopVault>(vault => vault.UseConfiguration(new OrdersName(Name)));

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(Name);
    }

    [Test]
    public async Task UseConfiguration_SameTypeTwice_ConfiguresOnce()
    {
        // Arrange
        var configuration = IVaultConfiguration<ShopVault>.Mock();
        await using var host = new VaultHost<ShopVault>(vault => vault
            .UseConfiguration(configuration.Object)
            .UseConfiguration(configuration.Object));

        // Act
        _ = host.Vault;

        // Assert — the builder is internal, so any builder is the one.
        configuration.Configure(Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task UseConfiguration_FromAConfiguration_IsApplied()
    {
        // Arrange
        const string Prefix = "nested_";
        await using var host = new VaultHost<ShopVault>(
            vault => vault.UseConfiguration<NestingConfiguration>(),
            services => services.AddSingleton(new NamingOptions(Prefix)));

        // Act
        var name = host.Vault.Audit.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(Prefix + nameof(ShopVault.Audit));
    }

    [Test]
    public async Task Configure_ConfigurableVault_RunsBeforeTheRegistration()
    {
        // Arrange
        const string Name = "from-registration";
        await using var configured = new VaultHost<ConfiguredVault>();
        await using var overridden = new VaultHost<ConfiguredVault>(vault => vault.Collection(x => x.Orders, orders => orders.Name(Name)));

        // Act
        var names = new
        {
            Configured = configured.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName,
            Overridden = overridden.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName
        };

        // Assert
        await Verify(names);
    }

    [Test]
    public async Task Configure_BaseClassConfiguringItsDerivedVault_Runs()
    {
        // Arrange
        await using var host = new VaultHost<DerivedVault>();

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(ConfiguringBase<DerivedVault>.OrdersName);
    }

    [Test]
    public async Task Configure_VaultConfiguringAnotherVault_IsNotCalled()
    {
        // Arrange
        await using var host = new VaultHost<MisdirectedVault>();

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(nameof(MisdirectedVault.Orders));
    }

    [Test]
    public async Task QueryFilter_FromEverySource_AppliesInOrder()
    {
        // Arrange — a default, the vault's own Configure, then the registration.
        await using var host = new VaultHost<ConfiguredVault>(
            vault => vault.Collection(x => x.Orders, orders => orders.QueryFilter(x => !x.IsDeleted)),
            services => services.AddDefaultVaultConfiguration(typeof(TotalFilterDefault<>)));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }
}

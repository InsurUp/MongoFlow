using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

public partial class VaultBuilderTests
{
    [Test]
    public async Task AddDefaultVaultConfiguration_NullServices_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => MongoVaultServiceCollectionExtensions.AddDefaultVaultConfiguration(null!, typeof(PrefixDefault<>)))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task AddDefaultVaultConfiguration_NullType_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.That(() => new ServiceCollection().AddDefaultVaultConfiguration(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task AddDefaultVaultConfiguration_ClosedType_ThrowsArgumentException()
    {
        // Act & Assert
        await Throws(() => new ServiceCollection().AddDefaultVaultConfiguration(typeof(ClosedConfiguration))).IgnoreStackTrace();
    }

    [Test]
    [Arguments(typeof(PrefixDefault<ShopVault>))]
    [Arguments(typeof(AbstractConfiguration<>))]
    [Arguments(typeof(IVaultConfiguration<>))]
    [Arguments(typeof(TwoParameterConfiguration<,>))]
    [Arguments(typeof(NotAConfiguration<>))]
    [Arguments(typeof(FixedVaultConfiguration<>))]
    public async Task AddDefaultVaultConfiguration_NotAnOpenConfigurationOfItsParameter_ThrowsArgumentException(Type type)
    {
        // Act & Assert
        await Assert.That(() => new ServiceCollection().AddDefaultVaultConfiguration(type)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task DefaultConfiguration_RegisteredBeforeOrAfterTheVaults_AppliesToEach()
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddSingleton(Offline.Client)
            .AddDefaultVaultConfiguration(typeof(PrefixDefault<>))
            .AddMongoVault<ShopVault>(vault => vault.UseDatabase(Offline.DatabaseName))
            .AddMongoVault<AuditedVault>(vault => vault.UseDatabase(Offline.DatabaseName))
            .AddDefaultVaultConfiguration(typeof(AuditNaming<>))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var shop = scope.ServiceProvider.GetRequiredService<ShopVault>();
        var audited = scope.ServiceProvider.GetRequiredService<AuditedVault>();

        // Act
        var names = new
        {
            ShopOrders = shop.Orders.MongoCollection.CollectionNamespace.CollectionName,
            ShopAudit = shop.Audit.MongoCollection.CollectionNamespace.CollectionName,
            AuditedOrders = audited.Orders.MongoCollection.CollectionNamespace.CollectionName,
            AuditedAudit = audited.Audit.MongoCollection.CollectionNamespace.CollectionName
        };

        // Assert — AuditNaming needs IAudited, which only AuditedVault implements; the vault's own name beats a default.
        await Verify(names);
    }

    [Test]
    public async Task SkipDefaultConfiguration_DefaultConfiguration_IsNotApplied()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(
            vault => vault.SkipDefaultConfiguration<PrefixDefault<ShopVault>>(),
            services => services.AddDefaultVaultConfiguration(typeof(PrefixDefault<>)));

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(nameof(ShopVault.Orders));
    }

    [Test]
    public async Task SkipAllDefaultConfigurations_DefaultConfigurations_AreNotApplied()
    {
        // Arrange
        var counter = new ApplyCounter();
        await using var host = new VaultHost<ShopVault>(
            vault => vault.SkipAllDefaultConfigurations(),
            services => services
                .AddSingleton(counter)
                .AddDefaultVaultConfiguration(typeof(PrefixDefault<>))
                .AddDefaultVaultConfiguration(typeof(CountingDefault<>)));

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(nameof(ShopVault.Orders));
        await Assert.That(counter.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SkipAllDefaultConfigurations_ThenUseConfiguration_AppliesThatOne()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(
            vault => vault
                .SkipAllDefaultConfigurations()
                .UseConfiguration<PrefixDefault<ShopVault>>(),
            services => services.AddDefaultVaultConfiguration(typeof(PrefixDefault<>)));

        // Act
        var name = host.Vault.Orders.MongoCollection.CollectionNamespace.CollectionName;

        // Assert
        await Assert.That(name).IsEqualTo(PrefixDefault<ShopVault>.Value + nameof(ShopVault.Orders));
    }

    [Test]
    public async Task UseConfiguration_OfADefaultConfiguration_AppliesItOnce()
    {
        // Arrange
        var counter = new ApplyCounter();
        await using var host = new VaultHost<ShopVault>(
            vault => vault.UseConfiguration<CountingDefault<ShopVault>>(),
            services => services
                .AddSingleton(counter)
                .AddDefaultVaultConfiguration(typeof(CountingDefault<>)));

        // Act
        _ = host.Vault;

        // Assert
        await Assert.That(counter.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SkipAllDefaultConfigurations_FromADefault_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(services: services =>
            services.AddDefaultVaultConfiguration(typeof(SkipAllDefault<>)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task SkipDefaultConfiguration_FromADefault_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(services: services =>
            services.AddDefaultVaultConfiguration(typeof(SkipOneDefault<>)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }
}

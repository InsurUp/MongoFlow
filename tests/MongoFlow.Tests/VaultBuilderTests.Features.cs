using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

public partial class VaultBuilderTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task UseSoftDelete_NullMember_ThrowsArgumentNullException(int overload)
    {
        // Arrange — one call per overload: a flag, a DateTime and a DateTimeOffset.
        await using var host = new VaultHost<ShopVault>(vault => _ = overload switch
        {
            0 => vault.UseSoftDelete((Expression<Func<Order, bool>>)null!),
            1 => vault.UseSoftDelete((Expression<Func<Order, DateTime?>>)null!),
            _ => vault.UseSoftDelete((Expression<Func<Order, DateTimeOffset?>>)null!)
        });

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task AddFeature_Type_IsCreatedFromTheRootProvider()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(
            vault => vault.AddFeature<TenantFeature>(),
            services => services.AddSingleton(new TenantOptions("t-1")));

        // Act
        var query = await host.Vault.Orders.QueryAsync();

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task AddFeature_FromAFeature_OwnsItsOwnFilters()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.AddFeature(new OuterFeature()));

        // Act
        var queries = new
        {
            BothOn = (await host.Vault.Orders.QueryAsync()).ToString(),
            InnerOff = (await host.Vault.Orders.Without(ActiveOnlyFeature.Key).QueryAsync()).ToString(),
            OuterOff = (await host.Vault.Orders.Without(OuterFeature.Key).QueryAsync()).ToString()
        };

        // Assert
        await Verify(queries);
    }

    [Test]
    public async Task AddFeature_DefaultKey_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.AddFeature(new UnnamedFeature()));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task AddFeature_ItsConfigurationFails_Throws()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(vault => vault.AddFeature(new FailingFeature()));

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<InvalidOperationException>();
    }
}

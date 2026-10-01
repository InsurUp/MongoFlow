namespace MongoFlow.Tests;

/// <summary>
/// <see cref="MongoVault"/>: a vault works only once MongoFlow created it, finds its collections by document type, and
/// saving nothing touches nothing.
/// </summary>
public class MongoVaultTests
{
    [Test]
    public async Task SaveAsync_VaultNotCreatedByMongoFlow_ThrowsInvalidOperationException()
    {
        // Arrange
        var vault = new ShopVault();

        // Act & Assert
        await ThrowsTask(() => vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task Collection_VaultNotCreatedByMongoFlow_ThrowsInvalidOperationException()
    {
        // Arrange
        var vault = new ShopVault();

        // Act & Assert
        await Assert.That(vault.Collection<Order>).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task SaveAsync_NothingQueued_ReturnsEmptyWithoutStartingATransaction()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();
        host.Vault.Audit.AddRange([]);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(SaveResult.Empty);
        await Assert.That(host.Transactions.Current).IsNull();
    }

    [Test]
    public async Task Collection_DeclaredDocument_ReturnsTheVaultsCollection()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var collection = host.Vault.Collection<AuditEntry>();

        // Assert
        await Assert.That(collection.MongoCollection).IsSameReferenceAs(host.Vault.Audit.MongoCollection);
    }

    [Test]
    public async Task Collection_KeyedDocument_ReturnsAKeyedCollection()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var collection = host.Vault.Collection<Order>();

        // Assert
        await Assert.That(collection).IsAssignableTo<IVaultCollection<Order, int>>();
    }

    [Test]
    public async Task Collection_UndeclaredDocument_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Throws(() => host.Vault.Collection<Uri>()).IgnoreStackTrace();
    }

    [Test]
    public async Task CollectionWithKey_DeclaredKey_ReturnsTheVaultsCollection()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var collection = host.Vault.Collection<Order, int>();

        // Assert
        await Assert.That(collection.MongoCollection).IsSameReferenceAs(host.Vault.Orders.MongoCollection);
    }

    [Test]
    public async Task CollectionWithKey_UndeclaredDocument_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => host.Vault.Collection<Uri, int>()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task CollectionWithKey_KeylessCollection_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Throws(() => host.Vault.Collection<AuditEntry, int>()).IgnoreStackTrace();
    }

    [Test]
    public async Task CollectionWithKey_AnotherKeyType_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => host.Vault.Collection<Order, string>()).ThrowsExactly<InvalidOperationException>();
    }
}

using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow.Tests;

/// <summary>
/// The scope's transaction, as <see cref="IVaultTransactionManager"/> and <see cref="IVaultTransaction"/> expose it:
/// <list type="number">
/// <item>one transaction is open per scope at a time, and it stops being current when it ends, however it ends;</item>
/// <item>its session starts with its first use, on the registered client, and commits, aborts and is disposed with
/// it;</item>
/// <item>once ended it can't be committed or used, and rolling it back again does nothing;</item>
/// <item>a vault on another client can't join it.</item>
/// </list>
/// </summary>
public class VaultTransactionTests : IAsyncDisposable
{
    private readonly Mock<IMongoClient> _client = IMongoClient.Mock();
    private readonly Mock<IClientSessionHandle> _session = IClientSessionHandle.Mock();
    private readonly VaultHost<ShopVault> _host;

    public VaultTransactionTests()
    {
        _client.StartSession(Any(), Any()).Returns(_session.Object);

        // The vault keeps the offline client, while the transaction starts on the mocked one registered in DI.
        _host = new VaultHost<ShopVault>(
            vault => vault.UseDatabase(Offline.Database),
            services => services.AddSingleton(_client.Object));
    }

    private IVaultTransactionManager Transactions => _host.Transactions;

    [Test]
    public async Task Current_NothingBegun_IsNull()
    {
        // Act
        var current = Transactions.Current;

        // Assert
        await Assert.That(current).IsNull();
    }

    [Test]
    public async Task BeginAsync_NothingOpen_BecomesCurrent()
    {
        // Act
        var transaction = await Transactions.BeginAsync();

        // Assert
        await Assert.That(Transactions.Current).IsSameReferenceAs(transaction);
    }

    [Test]
    public async Task BeginAsync_WhileOneIsOpen_ThrowsInvalidOperationException()
    {
        // Arrange
        await Transactions.BeginAsync();

        // Act & Assert
        await ThrowsTask(() => Transactions.BeginAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task CommitAsync_NothingJoined_EndsWithoutASession()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();

        // Act
        await transaction.CommitAsync();

        // Assert
        await Assert.That(Transactions.Current).IsNull();
        _client.StartSession(Any(), Any()).WasNeverCalled();
    }

    [Test]
    public async Task RollbackAsync_Open_EndsIt()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();

        // Act
        await transaction.RollbackAsync();

        // Assert
        await Assert.That(Transactions.Current).IsNull();
    }

    [Test]
    public async Task DisposeAsync_WithoutCommitting_EndsIt()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();

        // Act
        await transaction.DisposeAsync();

        // Assert
        await Assert.That(Transactions.Current).IsNull();
    }

    [Test]
    public async Task BeginAsync_AfterTheLastEnded_StartsANewOne()
    {
        // Arrange
        var ended = await Transactions.BeginAsync();
        await ended.RollbackAsync();

        // Act
        var transaction = await Transactions.BeginAsync();

        // Assert
        await Assert.That(transaction).IsNotSameReferenceAs(ended);
    }

    [Test]
    public async Task CommitAsync_AfterItEnded_ThrowsInvalidOperationException()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();
        await transaction.CommitAsync();

        // Act & Assert
        await ThrowsTask(() => transaction.CommitAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task RollbackAsync_AfterItEnded_DoesNothing()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();
        await transaction.CommitAsync();

        // Act
        await transaction.RollbackAsync();

        // Assert
        await Assert.That(Transactions.Current).IsNull();
    }

    [Test]
    public async Task Session_AfterItEnded_ThrowsInvalidOperationException()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();
        await transaction.RollbackAsync();

        // Act & Assert
        await Assert.That(() => transaction.Session).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Session_NoClientRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var services = new ServiceCollection()
            .AddMongoVault<ShopVault>(vault => vault.UseDatabase(Offline.Database))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var transaction = await scope.ServiceProvider.GetRequiredService<IVaultTransactionManager>().BeginAsync();

        // Act & Assert
        await Throws(() => transaction.Session).IgnoreStackTrace();
    }

    [Test]
    public async Task Session_FirstUse_StartsATransactionOnTheRegisteredClient()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();

        // Act
        var session = transaction.Session;

        // Assert
        await Assert.That(session).IsSameReferenceAs(_session.Object);
        _session.StartTransaction(Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task Session_SecondUse_ReturnsTheSameSession()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();
        var first = transaction.Session;

        // Act
        var second = transaction.Session;

        // Assert
        await Assert.That(second).IsSameReferenceAs(first);
        _client.StartSession(Any(), Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task CommitAsync_SessionInTransaction_CommitsIt()
    {
        // Arrange
        _session.IsInTransaction.Returns(true);
        var transaction = await Transactions.BeginAsync();
        _ = transaction.Session;

        // Act
        await transaction.CommitAsync();

        // Assert
        _session.CommitTransactionAsync(Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task CommitAsync_SessionNoLongerInTransaction_DoesNotCommit()
    {
        // Arrange
        _session.IsInTransaction.Returns(false);
        var transaction = await Transactions.BeginAsync();
        _ = transaction.Session;

        // Act
        await transaction.CommitAsync();

        // Assert
        _session.CommitTransactionAsync(Any()).WasNeverCalled();
    }

    [Test]
    public async Task CommitAsync_CommitFails_ThrowsAndEnds()
    {
        // Arrange
        _session.IsInTransaction.Returns(true);
        _session.CommitTransactionAsync(Any()).Throws(new MongoException("The commit failed."));
        var transaction = await Transactions.BeginAsync();
        _ = transaction.Session;

        // Act & Assert
        await Assert.That(() => transaction.CommitAsync()).ThrowsExactly<MongoException>();
        await Assert.That(Transactions.Current).IsNull();
    }

    [Test]
    public async Task RollbackAsync_SessionInTransaction_AbortsIt()
    {
        // Arrange
        _session.IsInTransaction.Returns(true);
        var transaction = await Transactions.BeginAsync();
        _ = transaction.Session;

        // Act
        await transaction.RollbackAsync();

        // Assert
        _session.AbortTransactionAsync(Any()).WasCalled(Times.Once);
    }

    [Test]
    public async Task DisposeAsync_WithASession_DisposesIt()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();
        _ = transaction.Session;

        // Act
        await transaction.DisposeAsync();

        // Assert
        _session.Dispose().WasCalled(Times.Once);
    }

    [Test]
    public async Task QueryAsync_InATransactionOnAnotherClient_ThrowsInvalidOperationException()
    {
        // Arrange
        var transaction = await Transactions.BeginAsync();
        _ = transaction.Session;

        // Act & Assert
        await ThrowsValueTask(() => _host.Vault.Orders.QueryAsync()).IgnoreStackTrace();
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

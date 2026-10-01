using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// The built-in concurrency token (<see cref="ConcurrencyTokenFeature"/>):
/// <list type="number">
/// <item>a replace, update or delete made with a document writes only if the stored token still has the value the
/// document was read with, and fails the save with <see cref="ConcurrencyException"/> otherwise, before other
/// interceptors see the write;</item>
/// <item>replaces and updates increment the token, stored and on the document; a failed or rolled-back save, including
/// an outer transaction's rollback, puts the document's token back to what was read;</item>
/// <item>writes by key or filter increment without checking, deletes by key or filter neither check nor increment, and
/// inserts are left alone;</item>
/// <item>a delete soft delete turns into an update is checked and incremented like an update;</item>
/// <item>with the feature off, writes are neither checked nor incremented.</item>
/// </list>
/// </summary>
public partial class ConcurrencyTokenTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task Replace_TokenAsRead_WritesAndIncrementsIt()
    {
        // Arrange
        await using var host = await SeededAsync(version: 3);
        var contract = new Contract { Id = 1, Party = "renamed", Version = 3 };
        host.Vault.Contracts.Replace(contract);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }

    [Test]
    public async Task Replace_StaleToken_ThrowsConcurrencyExceptionAndPutsTheTokenBack()
    {
        // Arrange
        await using var host = await SeededAsync(version: 4);
        var contract = new Contract { Id = 1, Party = "renamed", Version = 3 };
        host.Vault.Contracts.Replace(contract);

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Verify(new
        {
            exception!.Message,
            exception.DocumentExists,
            exception.Operation.Kind,
            Stored = await host.StoredAsync("Contracts"),
            Document = contract
        });
    }

    [Test]
    public async Task Replace_DocumentDeletedSinceItWasRead_ThrowsConcurrencyExceptionWithoutADocument()
    {
        // Arrange
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        host.Vault.Contracts.Replace(new Contract { Id = 1, Party = "renamed", Version = 3 });

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Verify(new { exception!.Message, exception.DocumentExists });
    }

    [Test]
    public async Task Update_TokenAsRead_AppliesTheUpdateAndIncrementsTheToken()
    {
        // Arrange
        await using var host = await SeededAsync(version: 3);
        var contract = new Contract { Id = 1, Party = "acme", Version = 3 };
        host.Vault.Contracts.Update(contract, Builders<Contract>.Update.Set(x => x.Party, "renamed"));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }

    [Test]
    public async Task Update_StaleToken_ThrowsConcurrencyException()
    {
        // Arrange
        const int Read = 3;
        await using var host = await SeededAsync(version: Read + 1);
        var contract = new Contract { Id = 1, Party = "acme", Version = Read };
        host.Vault.Contracts.Update(contract, Builders<Contract>.Update.Set(x => x.Party, "renamed"));

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Assert.That(contract.Version).IsEqualTo(Read);
    }

    [Test]
    public async Task UpdateByKeyAndUpdateMany_AnyStoredToken_IncrementWithoutChecking()
    {
        // Arrange
        await using var host = await SeededAsync(version: 7);
        await host.SeedAsync("Contracts", new Contract { Id = 2, Party = "globex", Version = 1 });
        host.Vault.Contracts.UpdateByKey(1, Builders<Contract>.Update.Set(x => x.Party, "by key"));
        host.Vault.Contracts.UpdateMany(x => x.Id > 0, Builders<Contract>.Update.Set(x => x.Party, "many"));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Contracts"));
    }

    [Test]
    public async Task Delete_TokenAsRead_DeletesTheDocument()
    {
        // Arrange
        await using var host = await SeededAsync(version: 3);
        host.Vault.Contracts.Delete(new Contract { Id = 1, Version = 3 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(await host.StoredAsync("Contracts")).IsEmpty();
    }

    [Test]
    public async Task Delete_StaleToken_ThrowsConcurrencyException()
    {
        // Arrange
        await using var host = await SeededAsync(version: 4);
        host.Vault.Contracts.Delete(new Contract { Id = 1, Version = 3 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Assert.That(await host.StoredAsync("Contracts")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DeleteByKeyAndDeleteMany_AnyStoredToken_DeleteWithoutChecking()
    {
        // Arrange
        await using var host = await SeededAsync(version: 4);
        await host.SeedAsync("Contracts", new Contract { Id = 2, Party = "globex", Version = 9 });
        host.Vault.Contracts.DeleteByKey(1);
        host.Vault.Contracts.DeleteMany(x => x.Party == "globex");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result.Deleted).IsEqualTo(2);
    }

    [Test]
    public async Task Add_VersionedDocument_LeavesTheTokenAlone()
    {
        // Arrange
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        var contract = new Contract { Id = 1, Party = "acme", Version = 5 };
        host.Vault.Contracts.Add(contract);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }

    [Test]
    public async Task UpdatesOfOneDocument_FailingAfterTheWrite_PutTheTokenBackToTheFirstRead()
    {
        // Arrange — the failing interceptor's hook after the write runs after the token's.
        await using var host = await SeededAsync(version: 3, vault => vault.AddInterceptor(new FailingAfterTheWrite()));
        var contract = new Contract { Id = 1, Party = "acme", Version = 3 };
        host.Vault.Contracts.Update(contract, Builders<Contract>.Update.Set(x => x.Party, "first"));
        host.Vault.Contracts.Update(contract, Builders<Contract>.Update.Set(x => x.Party, "second"));

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }

    [Test]
    public async Task SaveAsync_Conflict_FailsBeforeOtherInterceptorsSeeTheWrite()
    {
        // Arrange
        var log = new HookLog();
        await using var host = await SeededAsync(version: 4, vault => vault.AddInterceptor(new RecordingInterceptor("other", log)));
        host.Vault.Contracts.Replace(new Contract { Id = 1, Party = "renamed", Version = 3 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Verify(log.Entries);
    }

    [Test]
    public async Task RollbackAsync_SavesThatIncrementedTheToken_PutItBackToTheFirstRead()
    {
        // Arrange — two saves increment the same document.
        const int Read = 3;
        await using var host = await SeededAsync(version: Read);
        var transaction = await host.Transactions.BeginAsync();
        var contract = new Contract { Id = 1, Party = "acme", Version = Read };
        host.Vault.Contracts.Replace(contract);
        await host.Vault.SaveAsync();
        host.Vault.Contracts.Replace(contract);
        await host.Vault.SaveAsync();

        // Act
        await transaction.RollbackAsync();

        // Assert
        await Assert.That(contract.Version).IsEqualTo(Read);
    }

    [Test]
    public async Task Delete_OfASoftDeletableDocument_IsCheckedAndIncrementedLikeAnUpdate()
    {
        // Arrange
        await using var host = Mongo.Host<ContractVault>(UseTokensAndSoftDelete);
        await host.SeedAsync("Archives", new ArchivedContract { Id = 1, Version = 3 }, new ArchivedContract { Id = 2, Version = 5 });
        var current = new ArchivedContract { Id = 1, Version = 3 };
        host.Vault.Archives.Delete(current);
        await host.Vault.SaveAsync();
        host.Vault.Archives.Delete(new ArchivedContract { Id = 2, Version = 4 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Archives"), Document = current });
    }

    [Test]
    public async Task Replace_ThroughAViewWithTheFeatureOff_OverwritesWithoutCheckingOrIncrementing()
    {
        // Arrange
        await using var host = await SeededAsync(version: 4);
        var contract = new Contract { Id = 1, Party = "overwritten", Version = 1 };
        host.Vault.Contracts.Without(ConcurrencyTokenFeature.Key).Replace(contract);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }

    [Test]
    public async Task Replace_LongToken_IncrementsIt()
    {
        // Arrange — beyond what an int holds.
        const long Read = long.MaxValue - 1;
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        await host.SeedAsync("Deals", new Deal { Id = 1, Revision = Read });
        var deal = new Deal { Id = 1, Revision = Read };
        host.Vault.Deals.Replace(deal);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(deal.Revision).IsEqualTo(Read + 1);
    }

    [Test]
    public async Task Replace_UnversionedDocument_IsWrittenAsItIs()
    {
        // Arrange
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        await host.SeedAsync("Memos", new Memo { Id = 1, Text = "first" });
        host.Vault.Memos.Replace(new Memo { Id = 1, Text = "second" });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Memos"));
    }

    private static void UseTokens(IVaultBuilder<ContractVault> vault) => vault
        .UseConcurrencyToken((IVersioned x) => x.Version)
        .UseConcurrencyToken((IRevised x) => x.Revision);

    private static void UseTokensAndSoftDelete(IVaultBuilder<ContractVault> vault)
    {
        UseTokens(vault);
        vault.UseSoftDelete((IArchivable x) => x.IsArchived);
    }

    private async Task<VaultHost<ContractVault>> SeededAsync(int version,
        Action<IVaultBuilder<ContractVault>>? configure = null)
    {
        var host = Mongo.Host<ContractVault>(vault =>
        {
            UseTokens(vault);
            configure?.Invoke(vault);
        });

        await host.SeedAsync("Contracts", new Contract { Id = 1, Party = "acme", Version = version });
        return host;
    }
}

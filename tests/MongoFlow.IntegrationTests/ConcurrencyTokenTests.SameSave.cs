using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

// Writes made with a document the same save inserts.
public partial class ConcurrencyTokenTests
{
    [Test]
    public async Task Replace_DocumentTheSameSaveInserts_WritesItWithTheTokenAsAdded()
    {
        // Arrange
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        var contract = new Contract { Id = 1, Party = "acme" };
        host.Vault.Contracts.Add(contract);
        contract.Party = "renamed";
        host.Vault.Contracts.Replace(contract);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }

    [Test]
    public async Task Update_DocumentTheSameSaveInserts_AppliesTheUpdateAndLeavesTheToken()
    {
        // Arrange
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        var contract = new Contract { Id = 1, Party = "acme" };
        host.Vault.Contracts.Add(contract);
        host.Vault.Contracts.Update(contract, Builders<Contract>.Update.Set(x => x.Party, "renamed"));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }

    [Test]
    public async Task Delete_DocumentTheSameSaveInserts_DeletesIt()
    {
        // Arrange
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        var contract = new Contract { Id = 1, Party = "acme" };
        host.Vault.Contracts.Add(contract);
        host.Vault.Contracts.Delete(contract);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(await host.StoredAsync("Contracts")).IsEmpty();
    }

    [Test]
    public async Task Delete_SoftDeletableDocumentTheSameSaveInserts_MarksItWithTheTokenAsAdded()
    {
        // Arrange — soft delete turns the delete into an update, which would increment the token.
        await using var host = Mongo.Host<ContractVault>(UseTokensAndSoftDelete);
        var archived = new ArchivedContract { Id = 1 };
        host.Vault.Archives.Add(archived);
        host.Vault.Archives.Delete(archived);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Archives"), Document = archived });
    }

    [Test]
    public async Task Replace_AfterTheSaveThatInsertedAndReplacedTheDocument_IsCheckedAgainstTheStoredToken()
    {
        // Arrange — the first save inserts and replaces it; the document keeps the token the insert stored.
        await using var host = Mongo.Host<ContractVault>(UseTokens);
        var contract = new Contract { Id = 1, Party = "acme" };
        host.Vault.Contracts.Add(contract);
        host.Vault.Contracts.Replace(contract);
        await host.Vault.SaveAsync();
        contract.Party = "renamed";
        host.Vault.Contracts.Replace(contract);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Contracts"), Document = contract });
    }
}

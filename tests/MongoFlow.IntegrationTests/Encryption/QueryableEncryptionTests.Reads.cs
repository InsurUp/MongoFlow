using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow.IntegrationTests.Encryption;

public partial class QueryableEncryptionTests
{
    [Test]
    public async Task FindAsync_ByEncryptedMembers_FindsTheDocumentsDecrypted()
    {
        // Arrange
        await using var host = await SeededAsync();
        string[] ssns = ["111-11", "333-33"];

        // Act
        var found = new
        {
            Equal = await (await host.Vault.Customers.FindAsync(x => x.Ssn == "222-22")).Project(x => x.Id).ToListAsync(),
            NotEqual = await (await host.Vault.Customers.FindAsync(x => x.Ssn != "222-22")).SortBy(x => x.Id).Project(x => x.Id).ToListAsync(),
            Among = await (await host.Vault.Customers.FindAsync(x => ssns.Contains(x.Ssn))).SortBy(x => x.Id).Project(x => x.Id).ToListAsync(),
            Between = await (await host.Vault.Customers.FindAsync(x => x.Salary > 1000 && x.Salary <= 3000)).SortBy(x => x.Id).Project(x => x.Id).ToListAsync(),
            Above = await (await host.Vault.Customers.FindAsync(x => x.Salary >= 2500)).SortBy(x => x.Id).Project(x => x.Id).ToListAsync(),
            Exactly = await (await host.Vault.Customers.FindAsync(x => x.Salary == 2000)).Project(x => x.Id).ToListAsync(),
            Inside = await (await host.Vault.Customers.FindAsync(x => x.Address!.Street != null)).CountDocumentsAsync(),
            One = await host.Vault.Customers.FirstOrDefaultAsync(x => x.Ssn == "333-33")
        };

        // Assert
        await Verify(found);
    }

    [Test]
    public async Task FindAsync_NullEncryptedMember_FindsTheDocumentsWithout()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Customers.Add(new Customer { Id = 4, Name = "Grace" });
        await host.Vault.SaveAsync();

        // Act
        var found = new
        {
            Null = await (await host.Vault.Customers.FindAsync(x => x.Ssn == null)).Project(x => x.Id).ToListAsync(),
            NotNull = await (await host.Vault.Customers.FindAsync(x => x.Ssn != null)).CountDocumentsAsync()
        };

        // Assert
        await Verify(found);
    }

    [Test]
    public async Task QueryAsync_WhereOnEncryptedMembers_FindsTheDocuments()
    {
        // Arrange
        await using var host = await SeededAsync();
        var query = await host.Vault.Customers.QueryAsync();

        // Act
        var found = new
        {
            Where = await query.Where(x => x.Ssn == "222-22").Select(x => x.Name).ToListAsync(),
            Range = await query.Where(x => x.Salary >= 2000).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(),
            Both = await query.Where(x => x.Salary < 3000).Where(x => x.Ssn != "111-11").Select(x => x.Id).ToListAsync(),
            First = (await query.FirstOrDefaultAsync(x => x.Ssn == "333-33"))?.Name,
            Count = await query.CountAsync(x => x.Salary > 1000),
            Any = await query.AnyAsync(x => x.Ssn == "999-99"),
            Enumerated = query.Where(x => x.Ssn == "111-11").Select(x => x.Id).ToList()
        };

        // Assert
        await Verify(found);
    }

    [Test]
    public async Task ReadsAndWrites_QueryFilterOnAnEncryptedMember_SkipWhatItHides()
    {
        // Arrange — the vault hides blocked customers, by their encrypted identity number.
        await using var host = await HostAsync(customers: customers => customers.QueryFilter(x => x.Ssn != "000-00"));
        host.Vault.Customers.AddRange([new Customer { Id = 1, Ssn = "000-00" }, new Customer { Id = 2, Ssn = "222-22" }]);
        await host.Vault.SaveAsync();
        host.Vault.Customers.UpdateMany(x => true, Builders<Customer>.Update.Set(x => x.Name, "seen"));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Blocked = await host.Vault.Customers.GetByKeyAsync(1),
            Query = await (await host.Vault.Customers.QueryAsync()).Select(x => x.Id).ToListAsync(),
            Names = (await host.StoredAsync("Customers")).Select(document => document["Name"])
        });
    }

    [Test]
    public async Task QueryAsync_SortByAnEncryptedMember_ThrowsNotSupportedException()
    {
        // Arrange
        await using var host = await SeededAsync();
        var query = (await host.Vault.Customers.QueryAsync()).OrderBy(x => x.Salary);

        // Act & Assert
        await Throws(() => query.ToList()).IgnoreStackTrace();
    }

    [Test]
    public async Task QueryAsync_FilterAfterProjectingAnEncryptedMember_ThrowsNotSupportedException()
    {
        // Arrange — the projected value can't be told from another, so it can't be encrypted.
        await using var host = await SeededAsync();
        var query = (await host.Vault.Customers.QueryAsync()).Select(x => new { x.Id, x.Ssn }).Where(x => x.Ssn == "111-11");

        // Act & Assert
        await ThrowsTask(() => query.ToListAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task QueryAsync_ProjectingEncryptedMembers_ReadsThemDecrypted()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act
        var read = await (await host.Vault.Customers.QueryAsync())
            .Where(x => x.Salary > 1000)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Ssn, x.Address!.Street })
            .ToListAsync();

        // Assert
        await Verify(read);
    }

    [Test]
    public async Task FindAsync_MemberEncryptedWithoutQueries_ThrowsNotSupportedException()
    {
        // Arrange
        await using var host = await SeededAsync();

        // Act & Assert
        await ThrowsValueTask(() => host.Vault.Customers.FindAsync(x => x.Notes == "note 1")).IgnoreStackTrace();
    }

    [Test]
    public async Task MongoCollection_PlainValueOfAnEncryptedMember_IsRefusedByTheServer()
    {
        // Arrange — the driver's collection bypasses MongoFlow, which encrypts.
        await using var host = await SeededAsync();

        // Act
        var exception = await Assert.That(() => host.Vault.Customers.MongoCollection.InsertOneAsync(new Customer { Id = 9, Ssn = "plain" }))
            .Throws<MongoWriteException>();

        // Assert
        await Assert.That(exception!.WriteError.Code).IsEqualTo(121);
    }
}

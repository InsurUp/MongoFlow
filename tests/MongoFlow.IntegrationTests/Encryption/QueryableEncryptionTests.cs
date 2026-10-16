using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoFlow.Encryption;

namespace MongoFlow.IntegrationTests.Encryption;

/// <summary>
/// Queryable Encryption on MongoDB Community, with MongoFlow encrypting (<see cref="QueryableEncryptionFeature"/>):
/// <list type="number">
/// <item>saves store encrypted members encrypted, null ones absent, and read them back decrypted;</item>
/// <item>updates set encrypted members encrypted, and refuse what the server can't do to them;</item>
/// <item>set-based updates and deletes, soft delete's among them, write each matching document;</item>
/// <item>the concurrency token, change tracking and duplicate keys work as on any collection;</item>
/// <item>a save that fails writes nothing.</item>
/// </list>
/// </summary>
public partial class QueryableEncryptionTests
{
    private readonly LogSink _sink = new();

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task Add_EncryptedMembers_StoresThemEncryptedAndReadsThemBack()
    {
        // Arrange
        await using var host = await HostAsync();
        host.Vault.Customers.Add(new Customer
        {
            Id = 1, Name = "Ada", Ssn = "111-11", Salary = 5000, Notes = "pays on time",
            Address = new Address { City = "London", Street = "Baker Street" }
        });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Stored = await host.StoredAsync("Customers"),
            Read = await host.Vault.Customers.WithNoTracking().GetByKeyAsync(1)
        });
    }

    [Test]
    public async Task Add_NullEncryptedMembers_StoresThemAbsent()
    {
        // Arrange — the server can't encrypt null.
        await using var host = await HostAsync();
        host.Vault.Customers.Add(new Customer { Id = 1, Name = "Ada", Address = new Address { City = "London" } });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Customers"));
    }

    [Test]
    public async Task AddRange_ManyDocuments_WritesAndLogsEachOne()
    {
        // Arrange
        await using var host = await HostAsync(services: services => services.AddLogging(logging =>
            logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_sink)));
        host.Vault.Customers.AddRange(Customers(3));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Read = await (await host.Vault.Customers.FindAsync(x => true)).SortBy(x => x.Id).ToListAsync(),
            Logs = _sink.Snapshot(MongoFlowLogEvents.Save.Category)
        });
    }

    [Test]
    public async Task Replace_EncryptedMembers_StoresTheNewValues()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Customers.Replace(new Customer { Id = 1, Name = "Ada", Ssn = "999-99", Salary = 7000 });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Stored = await host.StoredAsync("Customers"),
            Read = await host.Vault.Customers.WithNoTracking().GetByKeyAsync(1)
        });
    }

    [Test]
    public async Task UpdateByKey_SetsEncryptedMembers_StoresThemEncrypted()
    {
        // Arrange — Notes set to null is unset, as null can't be encrypted.
        await using var host = await SeededAsync();
        host.Vault.Customers.UpdateByKey(1, Builders<Customer>.Update
            .Set(x => x.Ssn, "999-99")
            .Set(x => x.Notes, null)
            .Set(x => x.Address, new Address { City = "Paris", Street = "Rue de Rivoli" }));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Stored = await host.StoredAsync("Customers"),
            Read = await host.Vault.Customers.WithNoTracking().GetByKeyAsync(1)
        });
    }

    [Test]
    public async Task UpdateByKey_IncrementsAnEncryptedMember_ThrowsNotSupportedException()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Customers.UpdateByKey(1, Builders<Customer>.Update.Inc(x => x.Salary, 100));

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task UpdateByKey_Pipeline_ThrowsNotSupportedException()
    {
        // Arrange — the server refuses pipeline updates of an encrypted collection, even of members that aren't encrypted.
        await using var host = await SeededAsync();
        var customers = (IKeyedVaultCollection)host.Vault.Customers;
        customers.UpdateByKey(1, new BsonArray { new BsonDocument("$set", new BsonDocument("Name", "Ada L.")) });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task UpdateMany_ByAnEncryptedMember_UpdatesEachMatchingDocument()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Customers.UpdateMany(x => x.Salary >= 2000, Builders<Customer>.Update.Set(x => x.Ssn, "000-00"));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Read = await (await host.Vault.Customers.FindAsync(x => x.Ssn == "000-00")).SortBy(x => x.Id).ToListAsync()
        });
    }

    [Test]
    public async Task DeleteMany_ByAnEncryptedMember_DeletesEachMatchingDocument()
    {
        // Arrange
        await using var host = await SeededAsync(softDelete: false);
        host.Vault.Customers.DeleteMany(x => x.Ssn == "111-11" || x.Ssn == "333-33");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Customers") });
    }

    [Test]
    public async Task DeleteMany_SoftDeletable_MarksEachMatchingDocument()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Customers.DeleteMany(x => x.Salary < 3500);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Left = await (await host.Vault.Customers.QueryAsync()).Select(x => x.Id).ToListAsync(),
            Marked = await (await host.Vault.Customers.Without(SoftDeleteFeature.Key).FindAsync(x => x.IsDeleted)).Project(x => x.Id).ToListAsync()
        }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task Update_ReadWithAnOlderToken_ThrowsConcurrencyException()
    {
        // Arrange — another request changed the customer after it was read.
        await using var host = await SeededAsync();
        var customer = (await host.Vault.Customers.WithNoTracking().GetByKeyAsync(1))!;
        host.Vault.Customers.UpdateByKey(1, Builders<Customer>.Update.Set(x => x.Ssn, "222-22"));
        await host.Vault.SaveAsync();
        host.Vault.Customers.Update(customer, Builders<Customer>.Update.Set(x => x.Ssn, "999-99"));

        // Act & Assert
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<ConcurrencyException>();
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentChanged_UpdatesTheEncryptedMembers()
    {
        // Arrange — the street is set whole: the server refuses an update inside an encrypted member.
        await using var host = await SeededAsync(tracking: true);
        var customer = (await host.Vault.Customers.GetByKeyAsync(1))!;
        customer.Ssn = "999-99";
        customer.Address!.Street = "Abbey Road";

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            result,
            Read = await host.Vault.Customers.WithNoTracking().GetByKeyAsync(1)
        });
    }

    [Test]
    public async Task Add_RepeatedKey_ThrowsDuplicateKeyException()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Customers.Add(new Customer { Id = 4, Name = "Grace", Ssn = "444-44" });
        host.Vault.Customers.Add(new Customer { Id = 1, Name = "Ada again", Ssn = "111-11" });

        // Act
        var exception = await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<DuplicateKeyException>();

        // Assert — the first insert went in the same command, and was rolled back with it.
        await Verify(new
        {
            exception!.Operation.Kind,
            exception.Operation.Key,
            Stored = (await host.StoredAsync("Customers")).Select(document => document["_id"])
        });
    }

    [Test]
    public async Task Add_DocumentWithoutAnId_GetsOneAsTheDriverGivesIt()
    {
        // Arrange — MongoFlow serializes an encrypted document itself, so it generates the id first, as the driver would.
        var database = Mongo.NewDatabase();
        await using var host = new VaultHost<TicketVault>(Mongo.Client, database, vault =>
        {
            UseEncryption(vault, database.DatabaseNamespace.DatabaseName);
            vault.Collection(x => x.Tickets, tickets => tickets.Encrypt(x => x.Secret));
        });
        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
        var ticket = new Ticket { Secret = "open sesame" };
        host.Vault.Tickets.Add(ticket);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new
        {
            Generated = ticket.Id != ObjectId.Empty,
            Read = (await host.Vault.Tickets.GetByKeyAsync(ticket.Id))?.Secret
        });
    }

    [Test]
    [NotInParallel("failCommand")]
    public async Task SaveAsync_AWriteFails_WritesNothing()
    {
        // Arrange — the failpoint singles out this test's client, which the client that encrypts is made from.
        const string Client = "encryption-fails";
        await using var host = await HostAsync(client: Mongo.CreateClient(Client));
        host.Vault.Customers.Add(new Customer { Id = 1, Name = "Ada", Ssn = "111-11" });
        host.Vault.Customers.Add(new Customer { Id = 2, Name = "Grace", Ssn = "222-22" });
        host.Vault.Customers.UpdateByKey(1, Builders<Customer>.Update.Set(x => x.Name, "Ada L."));
        await Mongo.FailNextAsync("update", Client);

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).Throws<MongoCommandException>();

        // Assert
        await Verify(await host.StoredAsync("Customers")).DontIgnoreEmptyCollections();
    }

    private static IEnumerable<Customer> Customers(int count) =>
        Enumerable.Range(1, count).Select(id => new Customer
        {
            Id = id, Name = $"Customer {id}", Ssn = $"{id}{id}{id}-{id}{id}", Salary = id * 1000, Notes = $"note {id}",
            Address = new Address { City = "London", Street = $"{id} Baker Street" }
        });

    /// <summary>
    /// A <see cref="CustomerVault"/> with the customers encrypted, on a new database whose encrypted collections the
    /// migrator has set up; with soft delete, the concurrency token, and change tracking if asked.
    /// </summary>
    private async Task<VaultHost<CustomerVault>> HostAsync(bool softDelete = true,
        bool tracking = false,
        IMongoClient? client = null,
        Action<IServiceCollection>? services = null,
        Action<IVaultCollectionBuilder<Customer, int>>? customers = null)
    {
        client ??= Mongo.Client;
        var database = client.GetDatabase($"t{Guid.NewGuid():N}");

        var host = new VaultHost<CustomerVault>(client, database, vault =>
        {
            UseEncryption(vault, database.DatabaseNamespace.DatabaseName);
            vault.Collection(x => x.Customers, collection =>
                {
                    Encrypted(collection);
                    customers?.Invoke(collection);
                })
                .UseConcurrencyToken((IVersioned x) => x.Version)
                .UseChangeTracking(tracking);

            if (softDelete)
            {
                vault.UseSoftDelete((Customer x) => x.IsDeleted);
            }
        }, services);

        await host.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();
        return host;
    }

    /// <summary><see cref="HostAsync"/>, holding three customers.</summary>
    private async Task<VaultHost<CustomerVault>> SeededAsync(bool softDelete = true,
        bool tracking = false)
    {
        var host = await HostAsync(softDelete, tracking);
        await using var scope = host.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<CustomerVault>();

        vault.Customers.AddRange(Customers(3));
        await vault.SaveAsync();

        return host;
    }
}

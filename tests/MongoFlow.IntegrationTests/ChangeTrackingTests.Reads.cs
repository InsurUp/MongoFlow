using System.Collections;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow.IntegrationTests;

public partial class ChangeTrackingTests
{
    [Test]
    public async Task Reads_ReturningDocuments_TrackThem()
    {
        // Arrange — each customer is read another way.
        await using var host = Host();
        await host.SeedAsync("Customers", [.. Enumerable.Range(1, 12).Select(id => new Customer { Id = id, Name = $"c{id}" })]);
        var customers = host.Vault.Customers;
        var query = await customers.QueryAsync();
        var created = query.Provider.CreateQuery(query.Where(x => x.Id == 12).Expression);
        List<Customer> read =
        [
            (await customers.GetByKeyAsync(1))!,
            await (await customers.FindAsync(x => x.Id == 2)).FirstOrDefaultAsync(),
            .. await (await customers.FindAsync(x => x.Id >= 3)).SortBy(x => x.Id).Skip(0).Limit(1).ToListAsync(),
            .. (await customers.FindAsync(Builders<Customer>.Filter.Eq(x => x.Id, 4))).ToList(),
            .. await (await customers.QueryAsync()).Where(x => x.Id == 5).ToListAsync(),
            await (await customers.QueryAsync()).FirstOrDefaultAsync(x => x.Id == 6),
            .. (await customers.QueryAsync()).Where(x => x.Id == 7).ToList(),
            (await customers.QueryAsync()).First(x => x.Id == 8),
            .. (await customers.QueryAsync()).Where(x => x.Id == 9).ToCursor().ToList(),
            .. await (await customers.QueryAsync()).Where(x => x.Id == 10).Sample(1).ToListAsync(),
            .. await (await customers.QueryAsync()).OrderBy(x => x.Id).Where(x => x.Id >= 11).Take(1).ToListAsync(),
            .. Untyped(created)
        ];
        read.ForEach(customer => customer.Name = customer.Name.ToUpperInvariant());

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify((await host.StoredAsync("Customers")).Select(customer => customer["Name"]));
    }

    [Test]
    public async Task Reads_ReshapingDocuments_DontTrackThem()
    {
        // Arrange — projections, even to the document type, a stage of the app's own and an aggregation.
        await using var host = Host();
        await host.SeedAsync("Customers", [.. Enumerable.Range(1, 3).Select(id => new Customer { Id = id, Name = $"c{id}" })]);
        var customers = host.Vault.Customers;
        var projected = await customers.FindAsync(x => x.Id == 1);
        projected.Options.Projection = Builders<Customer>.Projection.Expression(x => new Customer { Id = x.Id, Name = x.Name });
        var match = new BsonDocumentPipelineStageDefinition<Customer, Customer>(new BsonDocument("$match", new BsonDocument("_id", 3)));
        List<Customer> read =
        [
            .. await (await customers.QueryAsync()).Select(x => new Customer { Id = x.Id, Name = x.Name }).ToListAsync(),
            .. (await customers.QueryAsync()).Select(x => new Customer { Id = x.Id, Name = x.Name }).ToList(),
            .. (await customers.QueryAsync()).Select(x => new Customer { Id = x.Id, Name = x.Name }).ToCursor().ToList(),
            await (await customers.QueryAsync()).Select(x => new Customer { Id = x.Id, Name = x.Name }).FirstAsync(),
            .. await (await customers.QueryAsync()).AppendStage(match).ToListAsync(),
            .. await (await customers.FindAsync(x => x.Id == 2)).Project(x => new Customer { Id = x.Id, Name = x.Name }).ToListAsync(),
            .. await projected.ToListAsync(),
            .. await (await customers.AggregateAsync()).ToListAsync()
        ];
        read.ForEach(customer => customer.Name = "changed");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(SaveResult.Empty);
    }

    [Test]
    public async Task QueryAsync_FirstOrDefaultAsyncFindingNothing_ReturnsNull()
    {
        // Arrange
        await using var host = Host();
        var query = await host.Vault.Customers.QueryAsync();

        // Act
        var customer = await query.FirstOrDefaultAsync(x => x.Id == 99);

        // Assert
        await Assert.That(customer).IsNull();
    }

    [Test]
    public async Task WithNoTracking_VaultTracks_ReadsUntracked()
    {
        // Arrange
        await using var host = Host();
        await host.SeedAsync("Customers", new Customer { Id = 1, Name = "ada" }, new Customer { Id = 2, Name = "bob" });
        var untracked = host.Vault.Customers.WithNoTracking();
        List<Customer> read = [(await untracked.GetByKeyAsync(1))!, .. await (await untracked.QueryAsync()).ToListAsync()];
        read.ForEach(customer => customer.Name = "changed");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(SaveResult.Empty);
    }

    [Test]
    public async Task WithTracking_VaultDoesntTrack_TracksOnlyWhatItReads()
    {
        // Arrange
        await using var host = Host(vault => vault.UseChangeTracking(false));
        await host.SeedAsync("Customers", new Customer { Id = 1, Name = "ada" }, new Customer { Id = 2, Name = "bob" });
        var untracked = (await host.Vault.Customers.GetByKeyAsync(1))!;
        var tracked = (await host.Vault.Customers.WithTracking().GetByKeyAsync(2))!;
        untracked.Name = "Ada Lovelace";
        tracked.Name = "Bob Dylan";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Customers"));
    }

    [Test]
    public async Task TrackedReads_TheirOtherMembers_ActAsTheDriversDo()
    {
        // Arrange
        await using var host = Host();
        await host.SeedAsync("Customers", [.. Enumerable.Range(1, 3).Select(id => new Customer { Id = id, Name = $"c{id}" })]);
        var find = await host.Vault.Customers.FindAsync(x => x.Id > 0);
        var query = await host.Vault.Customers.QueryAsync();
        var count = Expression.Call(typeof(Queryable), nameof(Queryable.Count), [typeof(Customer)], query.Expression);

        // Act
#pragma warning disable CS0618 // The obsolete counts are part of the driver's find, so they still delegate.
        var members = new
        {
            Counts = new[] { find.CountDocuments(), await find.CountDocumentsAsync(), find.Count(), await find.CountAsync() },
            Filter = find.Filter.Render(new RenderArgs<Customer>(host.Vault.Customers.MongoCollection.DocumentSerializer,
                host.Vault.Customers.MongoCollection.Settings.SerializerRegistry)),
            Sorted = (await find.Sort(Builders<Customer>.Sort.Descending(x => x.Id)).As<BsonDocument>().ToListAsync())
                .Select(customer => customer["_id"]),
            Counted = query.Provider.Execute<int>(count),
            LoggedStages = ((IMongoQueryProvider)query.Provider).LoggedStages,
            query.ElementType
        };
#pragma warning restore CS0618

        // Assert
        await Verify(members);
    }

    [Test]
    public async Task TrackedReads_UntypedExecute_ThrowsAsTheDriversDoes()
    {
        // Arrange
        await using var host = Host();
        var query = await host.Vault.Customers.QueryAsync();
        var count = Expression.Call(typeof(Queryable), nameof(Queryable.Count), [typeof(Customer)], query.Expression);

        // Act & Assert
        await Assert.That(() => query.Provider.Execute(count)).ThrowsExactly<NotImplementedException>();
    }

    [Test]
    public async Task TrackedReads_FilterSetOnTheFind_FindsWhatItMatches()
    {
        // Arrange
        await using var host = Host();
        await host.SeedAsync("Customers", [.. Enumerable.Range(1, 3).Select(id => new Customer { Id = id, Name = $"c{id}" })]);
        var find = await host.Vault.Customers.FindAsync(x => x.Id > 0);
        find.Filter = Builders<Customer>.Filter.Eq(x => x.Id, 2);
        var customer = await find.SingleAsync();
        customer.Name = "changed";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify((await host.StoredAsync("Customers")).Select(stored => stored["Name"]));
    }

    [Test]
    public async Task GetByKeyAsync_FromParallelTasks_TracksEveryDocument()
    {
        // Arrange
        await using var host = Host();
        await host.SeedAsync("Customers", [.. Enumerable.Range(1, 20).Select(id => new Customer { Id = id, Name = $"c{id}" })]);
        var read = await Task.WhenAll(Enumerable.Range(1, 20).Select(id => host.Vault.Customers.GetByKeyAsync(id)));
        foreach (var customer in read)
        {
            customer!.Name = "changed";
        }

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result.Modified).IsEqualTo(20);
    }

    [Test]
    public async Task FindAsync_DocumentWithoutItsKey_IsntTracked()
    {
        // Arrange — a coupon is keyed by its code, which one lacks.
        await using var host = Host();
        await host.SeedAsync("Coupons",
            new Coupon { Id = ObjectId.Parse("652f1c2e9b1d4a3f8c7e6d01"), Code = "A", Percent = 10 },
            new Coupon { Id = ObjectId.Parse("652f1c2e9b1d4a3f8c7e6d02"), Percent = 20 });
        var coupons = await (await host.Vault.Coupons.FindAsync(_ => true)).ToListAsync();
        coupons.ForEach(coupon => coupon.Percent += 5);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Coupons"));
    }

    // Enumerates a query through its untyped enumerator, as code that only knows IQueryable does.
    private static IEnumerable<Customer> Untyped(IQueryable query)
    {
        foreach (var customer in (IEnumerable)query)
        {
            yield return (Customer)customer;
        }
    }
}

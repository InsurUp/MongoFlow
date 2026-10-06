using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A vault's collections without their type arguments, against the server:
/// <list type="number">
/// <item>writes save as the typed ones do, with filters and updates as BSON, by element name, joined with the query
/// filters;</item>
/// <item><c>GetByKeyAsync</c> reads what the query filters let it see, tracking it or not as its view says;</item>
/// <item>an interceptor that has only an operation's document type reads the document the write targets.</item>
/// </list>
/// </summary>
public partial class UntypedCollectionTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task SaveAsync_EachKindOfUntypedWrite_StoresIt()
    {
        // Arrange — the number is stored as "number", and the query filter hides P-5 from every write.
        await using var host = Mongo.Host<InsuranceVault>(vault => vault
            .Collection(x => x.Policies, policies => policies.QueryFilter(x => x.Holder != "hidden")));
        await host.SeedAsync("Policies",
            new Policy { Id = 1, Number = "P-1", Holder = "ada" },
            new Policy { Id = 2, Number = "P-2", Holder = "bob" },
            new Policy { Id = 3, Number = "P-3", Holder = "cy" },
            new Policy { Id = 4, Number = "P-4", Holder = "dee" },
            new Policy { Id = 5, Number = "P-5", Holder = "hidden" },
            new Policy { Id = 6, Number = "P-6", Holder = "eve" },
            new Policy { Id = 7, Number = "P-7", Holder = "fay" },
            new Policy { Id = 8, Number = "P-8", Holder = "gil" });
        var policies = host.Vault.KeyedCollection(typeof(Policy));
        policies.Add(new Policy { Id = 9, Number = "P-9", Holder = "ivy" });
        policies.AddRange([new Policy { Id = 10, Number = "P-10", Holder = "jo" }]);
        policies.Replace(new Policy { Id = 1, Number = "P-1", Holder = "ada, replaced" });
        policies.Update(new Policy { Id = 2, Number = "P-2" }, Set("Holder", "bob, updated"));
        policies.UpdateByKey("P-3", Set("Holder", "cy, updated by key"));
        policies.UpdateMany(new BsonDocument("number", new BsonDocument("$in", new BsonArray { "P-4", "P-5" })),
            new BsonArray { Set("Holder", new BsonDocument("$concat", new BsonArray { "$Holder", ", updated in a pipeline" })) });
        policies.Delete(new Policy { Id = 6, Number = "P-6" });
        policies.DeleteByKey("P-7");
        policies.DeleteMany(new BsonDocument("Holder", new BsonDocument("$in", new BsonArray { "gil", "hidden" })));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Policies") });
    }

    [Test]
    public async Task GetByKeyAsync_UntypedView_ReturnsWhatTheQueryFiltersShow()
    {
        // Arrange
        await using var host = Mongo.Host<InsuranceVault>(vault => vault
            .Collection(x => x.Policies, policies => policies.QueryFilter(x => x.Holder != "hidden")));
        await host.SeedAsync("Policies",
            new Policy { Id = 1, Number = "P-1", Holder = "ada" },
            new Policy { Id = 2, Number = "P-2", Holder = "hidden" });
        var policies = host.Vault.KeyedCollection(typeof(Policy));

        // Act
        object?[] read = [await policies.GetByKeyAsync("P-1"), await policies.GetByKeyAsync("P-2")];

        // Assert
        await Verify(read);
    }

    [Test]
    public async Task WithTrackingAndWithNoTracking_UntypedViews_DecideWhetherAReadDocumentIsSaved()
    {
        // Arrange — the vault doesn't track changes, so only the view that asks for it tracks what it reads.
        await using var host = Mongo.Host<InsuranceVault>();
        await host.SeedAsync("Policies",
            new Policy { Id = 1, Number = "P-1", Holder = "ada" },
            new Policy { Id = 2, Number = "P-2", Holder = "bob" });
        var policies = host.Vault.KeyedCollection(typeof(Policy));
        var tracked = (Policy)(await policies.WithTracking().GetByKeyAsync("P-1"))!;
        var untracked = (Policy)(await policies.WithTracking().WithNoTracking().GetByKeyAsync("P-2"))!;
        tracked.Holder = "ada, changed";
        untracked.Holder = "bob, changed";

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Policies"));
    }

    [Test]
    public async Task GetByKeyAsync_FromAnInterceptor_ReadsTheDocumentEachWriteTargets()
    {
        // Arrange — the interceptor knows only each operation's document type. P-2 isn't stored.
        var reader = new TargetReader();
        await using var host = Mongo.Host<InsuranceVault>(vault => vault.AddInterceptor(reader));
        await host.SeedAsync("Policies", new Policy { Id = 1, Number = "P-1", Holder = "ada" });
        host.Vault.Policies.UpdateByKey("P-1", Builders<Policy>.Update.Set(x => x.Holder, "bob"));
        host.Vault.Policies.DeleteByKey("P-2");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(reader.Targets);
    }

    private static BsonDocument Set(string field,
        BsonValue value) =>
        new("$set", new BsonDocument(field, value));
}

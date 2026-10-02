using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// The built-in multi-tenancy (<see cref="MultiTenancyFeature"/>), over tenant ids of a reference type, a nullable
/// struct and a struct that isn't nullable:
/// <list type="number">
/// <item>reads see only the current tenant's documents, or with no current tenant only those without one;</item>
/// <item>inserts and replaces without a tenant get the current one, and those of another tenant fail the save, as do
/// updates made with a document of another tenant, a tracked one's changes included, and those whose update changes the
/// tenant field;</item>
/// <item>other updates and deletes are left to the query filters, and with no current tenant nothing is stamped or
/// checked;</item>
/// <item>for all tenants, and with the feature off, reads see every tenant and writes go unchecked.</item>
/// </list>
/// </summary>
public partial class MultiTenancyTests
{
    private static readonly Guid AgencyA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgencyB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task Add_WithoutATenant_GetsTheCurrentOne()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        var invoice = new Invoice { Id = 1, Number = "I-1" };
        host.Vault.Invoices.Add(invoice);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Invoices"), Document = invoice });
    }

    [Test]
    public async Task Add_OfTheCurrentTenant_IsWritten()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        host.Vault.Invoices.Add(new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result.Inserted).IsEqualTo(1);
    }

    [Test]
    public async Task Add_OfAnotherTenant_FailsTheSave()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        host.Vault.Invoices.Add(new Invoice { Id = 1, Number = "I-1", TenantId = "t-2" });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
        await Assert.That(await host.StoredAsync("Invoices")).IsEmpty();
    }

    [Test]
    public async Task Replace_WithoutATenantOrOfAnotherOne_IsStampedOrFails()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });
        var stamped = new Invoice { Id = 1, Number = "I-1 revised" };
        host.Vault.Invoices.Replace(stamped);
        await host.Vault.SaveAsync();
        host.Vault.Invoices.Replace(new Invoice { Id = 1, Number = "I-1 moved", TenantId = "t-2" });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Invoices"), Document = stamped });
    }

    [Test]
    public async Task Reads_WithACurrentTenant_SeeOnlyItsDocuments()
    {
        // Arrange
        await using var host = await SeededAsync(new CurrentTenant { Id = "t-1" });

        // Act
        var ids = await (await host.Vault.Invoices.QueryAsync()).Select(x => x.Id).ToListAsync();

        // Assert
        await Assert.That(ids).IsEquivalentTo([1]);
    }

    [Test]
    public async Task Reads_WithoutACurrentTenant_SeeOnlyDocumentsWithoutOne()
    {
        // Arrange
        await using var host = await SeededAsync(new CurrentTenant());

        // Act
        var ids = await (await host.Vault.Invoices.QueryAsync()).Select(x => x.Id).ToListAsync();

        // Assert
        await Assert.That(ids).IsEquivalentTo([3]);
    }

    [Test]
    public async Task Reads_ForAllTenantsOrWithTheFeatureOff_SeeEveryTenant()
    {
        // Arrange
        await using var all = await SeededAsync(new CurrentTenant { Id = "t-1", All = true });
        await using var off = await SeededAsync(new CurrentTenant { Id = "t-1" });

        // Act
        var reads = new
        {
            All = await (await all.Vault.Invoices.QueryAsync()).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(),
            Off = await (await off.Vault.Invoices.Without(MultiTenancyFeature.Key).QueryAsync()).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync()
        };

        // Assert
        await Verify(reads);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Writes_WithoutACurrentTenantOrForAllTenants_AreNeitherStampedNorChecked(bool allTenants)
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = allTenants ? "t-1" : null, All = allTenants });
        host.Vault.Invoices.Add(new Invoice { Id = 1, Number = "I-1" });
        host.Vault.Invoices.Add(new Invoice { Id = 2, Number = "I-2", TenantId = "t-2" });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Invoices"));
    }

    [Test]
    public async Task UpdateManyAndDeleteByKey_OtherTenantsDocuments_AreLeftAlone()
    {
        // Arrange
        await using var host = await SeededAsync(new CurrentTenant { Id = "t-1" });
        host.Vault.Invoices.UpdateMany(x => x.Id > 0, Builders<Invoice>.Update.Set(x => x.Number, "updated"));
        host.Vault.Invoices.DeleteByKey(2);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Invoices") });
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentMovedToAnotherTenant_FailsTheSave()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" }, vault => vault.UseChangeTracking());
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });
        var invoice = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        invoice.TenantId = "t-2";

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(await host.StoredAsync("Invoices"));
    }

    [Test]
    public async Task SaveAsync_TrackedDocumentWhoseTenantIsCleared_FailsTheSave()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" }, vault => vault.UseChangeTracking());
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });
        var invoice = (await host.Vault.Invoices.GetByKeyAsync(1))!;
        invoice.TenantId = null;

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(await host.StoredAsync("Invoices"));
    }

    [Test]
    [Arguments("another tenant")]
    [Arguments("no tenant")]
    [Arguments("a field inside the tenant")]
    public async Task Update_ADocumentWithAnUpdateMovingItOutOfTheTenant_FailsTheSave(string update)
    {
        // Arrange — the document carries the current tenant; its update moves it out.
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });
        var invoice = new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" };
        host.Vault.Invoices.Update(invoice, update switch
        {
            "another tenant" => Builders<Invoice>.Update.Set(x => x.TenantId, "t-2"),
            "no tenant" => Builders<Invoice>.Update.Unset(x => x.TenantId),
            _ => Builders<Invoice>.Update.Set("TenantId.code", "t-2")
        });

        // Act & Assert
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Update_ADocumentWithAPipeline_IsWrittenUnread()
    {
        // Arrange — an aggregation pipeline isn't read for the tenant field.
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });
        var invoice = new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" };
        PipelineDefinition<Invoice, Invoice> pipeline = new[] { new BsonDocument("$set", new BsonDocument("Number", "I-1 revised")) };
        host.Vault.Invoices.Update(invoice, new PipelineUpdateDefinition<Invoice>(pipeline));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result.Modified).IsEqualTo(1);
    }

    [Test]
    public async Task Update_ADocumentWithAnUpdateToTheCurrentTenant_IsWritten()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });
        var invoice = new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" };
        host.Vault.Invoices.Update(invoice, Builders<Invoice>.Update.Set(x => x.TenantId, "t-1").Set(x => x.Number, "I-1 revised"));

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Assert.That(result.Modified).IsEqualTo(1);
    }

    [Test]
    public async Task Update_DocumentWithoutATenant_IsWrittenAndLeftWithout()
    {
        // Arrange — an update writes only what it says, so a tenant stamped on its document wouldn't be stored.
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" });
        var invoice = new Invoice { Id = 1 };
        host.Vault.Invoices.Update(invoice, Builders<Invoice>.Update.Set(x => x.Number, "I-1 revised"));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Invoices"), Document = invoice });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Add_NullableStructTenantUnset_GetsTheCurrentOne(bool empty)
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Agency = AgencyA });
        var quote = new Quote { Id = 1, AgencyId = empty ? Guid.Empty : null };
        host.Vault.Quotes.Add(quote);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(quote.AgencyId).IsEqualTo(AgencyA);
    }

    [Test]
    public async Task Add_NullableStructTenantOfAnotherAgency_FailsTheSave()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Agency = AgencyA });
        host.Vault.Quotes.Add(new Quote { Id = 1, AgencyId = AgencyB });

        // Act & Assert
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Add_StructTenantLeftAtItsDefault_GetsTheCurrentOne()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Agency = AgencyA });
        await host.SeedAsync("Visits", new Visit { Id = 1, AgencyId = AgencyB });
        var visit = new Visit { Id = 2 };
        host.Vault.Visits.Add(visit);
        await host.Vault.SaveAsync();

        // Act
        var ids = await (await host.Vault.Visits.QueryAsync()).Select(x => x.Id).ToListAsync();

        // Assert
        await Assert.That(visit.AgencyId).IsEqualTo(AgencyA);
        await Assert.That(ids).IsEquivalentTo([2]);
    }

    [Test]
    public async Task UseMultiTenancy_WithoutAnAllTenantsCallback_LimitsReadsAndStampsWrites()
    {
        // Arrange
        await using var host = Mongo.Host<TenantVault>(
            vault => vault.UseMultiTenancy((ITenantOwned x) => x.TenantId, Current(t => t.Id)),
            services => services.AddScoped(_ => new CurrentTenant { Id = "t-1" }));
        await host.SeedAsync("Invoices", new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" }, new Invoice { Id = 2, Number = "I-2", TenantId = "t-2" });
        var invoice = new Invoice { Id = 3, Number = "I-3" };
        host.Vault.Invoices.Add(invoice);
        await host.Vault.SaveAsync();

        // Act
        var ids = await (await host.Vault.Invoices.QueryAsync()).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();

        // Assert
        await Verify(new { Visible = ids, Stamped = invoice.TenantId });
    }

    [Test]
    public async Task Add_NotTenantOwned_IsLeftAlone()
    {
        // Arrange
        await using var host = Host(new CurrentTenant { Id = "t-1" });
        host.Vault.Countries.Add(new Country { Id = 1, Name = "Türkiye" });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Countries"));
    }

    private VaultHost<TenantVault> Host(CurrentTenant tenant,
        Action<IVaultBuilder<TenantVault>>? configure = null) =>
        Mongo.Host<TenantVault>(
            vault =>
            {
                vault
                    .UseMultiTenancy((ITenantOwned x) => x.TenantId, Current(t => t.Id), All)
                    .UseMultiTenancy((IAgencyOwned x) => x.AgencyId, Current(t => t.Agency), All)
                    .UseMultiTenancy((IVisitOwned x) => x.AgencyId, Current(t => t.Agency), All);
                configure?.Invoke(vault);
            },
            services => services.AddScoped(_ => tenant));

    private async Task<VaultHost<TenantVault>> SeededAsync(CurrentTenant tenant)
    {
        var host = Host(tenant);
        await host.SeedAsync("Invoices",
            new Invoice { Id = 1, Number = "I-1", TenantId = "t-1" },
            new Invoice { Id = 2, Number = "I-2", TenantId = "t-2" },
            new Invoice { Id = 3, Number = "I-3" });

        return host;
    }

    private static Func<IServiceProvider, T> Current<T>(Func<CurrentTenant, T> read) =>
        services => read(services.GetRequiredService<CurrentTenant>());

    private static bool All(IServiceProvider services) => services.GetRequiredService<CurrentTenant>().All;
}

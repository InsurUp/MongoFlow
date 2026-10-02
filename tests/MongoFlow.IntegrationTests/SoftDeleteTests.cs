using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// The built-in soft delete (<see cref="SoftDeleteFeature"/>):
/// <list type="number">
/// <item>every kind of delete on a soft-deletable collection marks the stored documents instead of removing them, and
/// marks a deleted document too;</item>
/// <item>reads skip marked documents;</item>
/// <item>a deletion timestamp comes from the <see cref="TimeProvider"/> in DI, or the system clock;</item>
/// <item>with the feature off, on a view or for a whole collection, and on other collections, deletes remove.</item>
/// </list>
/// </summary>
public partial class SoftDeleteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 30, 0, TimeSpan.Zero);

    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task Delete_SoftDeletableDocument_MarksItAndTheStoredOne()
    {
        // Arrange
        await using var host = await SeededAsync();
        var article = new Article { Id = 1, Title = "first" };
        host.Vault.Articles.Delete(article);

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Articles"), Document = article });
    }

    [Test]
    public async Task DeleteByKeyAndDeleteMany_SoftDeletable_MarkTheStoredDocuments()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Articles.DeleteByKey(1);
        host.Vault.Articles.DeleteMany(x => x.Title == "second");

        // Act
        var result = await host.Vault.SaveAsync();

        // Assert
        await Verify(new { result, Stored = await host.StoredAsync("Articles") });
    }

    [Test]
    public async Task Reads_SoftDeletable_SkipMarkedDocumentsUnlessTheFeatureIsOff()
    {
        // Arrange
        await using var host = await SeededAsync();
        await host.SeedAsync("Articles", new Article { Id = 3, Title = "deleted", IsDeleted = true });

        // Act
        var reads = new
        {
            On = await (await host.Vault.Articles.QueryAsync()).Select(x => x.Id).ToListAsync(),
            Off = await (await host.Vault.Articles.Without(SoftDeleteFeature.Key).QueryAsync()).Select(x => x.Id).ToListAsync()
        };

        // Assert
        await Verify(reads);
    }

    [Test]
    public async Task Delete_ThroughAViewWithTheFeatureOff_RemovesTheDocument()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Articles.Without(SoftDeleteFeature.Key).DeleteByKey(1);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Articles"));
    }

    [Test]
    public async Task Delete_InACollectionThatOptedOut_RemovesTheDocument()
    {
        // Arrange
        await using var host = await SeededAsync(vault => vault.Collection(x => x.Articles, articles => articles.Without(SoftDeleteFeature.Key)));
        host.Vault.Articles.DeleteByKey(1);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Articles"));
    }

    [Test]
    public async Task Delete_NotSoftDeletable_RemovesTheDocument()
    {
        // Arrange
        await using var host = await SeededAsync();
        await host.SeedAsync("Tags", new Tag { Id = 1, Name = "news" });
        host.Vault.Tags.DeleteByKey(1);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(await host.StoredAsync("Tags")).IsEmpty();
    }

    [Test]
    public async Task Save_WithoutDeletes_LeavesTheWritesAlone()
    {
        // Arrange
        await using var host = await SeededAsync();
        host.Vault.Articles.Add(new Article { Id = 3, Title = "third" });
        host.Vault.Articles.UpdateByKey(1, Builders<Article>.Update.Set(x => x.Title, "renamed"));

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(await host.StoredAsync("Articles"));
    }

    [Test]
    public async Task Delete_DeletedAt_StampsTheTimeProvidersTime()
    {
        // Arrange
        await using var host = await SeededAsync(services: services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now)));
        await host.SeedAsync("Comments", new Comment { Id = 1, Text = "first" });
        var comment = new Comment { Id = 1, Text = "first" };
        host.Vault.Comments.Delete(comment);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { Stored = await host.StoredAsync("Comments"), Document = comment }).DontScrubDateTimes();
    }

    [Test]
    public async Task Delete_DeletedAtWithoutATimeProvider_StampsTheSystemTime()
    {
        // Arrange
        await using var host = await SeededAsync();
        await host.SeedAsync("Comments", new Comment { Id = 1, Text = "first" });
        var comment = new Comment { Id = 1, Text = "first" };
        host.Vault.Comments.Delete(comment);
        var before = DateTime.UtcNow;

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(comment.DeletedAt).IsNotNull().And.IsBetween(before, DateTime.UtcNow);
    }

    [Test]
    public async Task Delete_RemovedAtOffset_StampsTheTimeProvidersTime()
    {
        // Arrange
        await using var host = await SeededAsync(services: services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now)));
        await host.SeedAsync("Attachments", new Attachment { Id = 1, Name = "report.pdf" });
        var attachment = new Attachment { Id = 1, Name = "report.pdf" };
        host.Vault.Attachments.Delete(attachment);

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(attachment.RemovedAt).IsEqualTo(Now);
        await Assert.That(await (await host.Vault.Attachments.QueryAsync()).AnyAsync()).IsFalse();
    }

    [Test]
    public async Task DeleteByKey_QueuedFromAnotherTaskWhileASaveRuns_WaitsForTheNextSave()
    {
        // Arrange — the save holds in an interceptor that runs after soft delete's, while another task queues a delete,
        // which joining that save would skip soft delete and make real.
        var gate = new SaveTests.Gate();
        await using var host = await SeededAsync(vault => vault.AddInterceptor(gate));
        host.Vault.Tags.Add(new Tag { Id = 1, Name = "news" });
        var first = host.Vault.SaveAsync();
        await gate.Entered.Task;
        await Task.Run(() => host.Vault.Articles.DeleteByKey(1));
        gate.Release.SetResult();
        await first;
        var afterFirst = await host.StoredAsync("Articles");

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(new { AfterFirst = afterFirst, AfterSecond = await host.StoredAsync("Articles") });
    }

    private async Task<VaultHost<BlogVault>> SeededAsync(Action<IVaultBuilder<BlogVault>>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        var host = Mongo.Host<BlogVault>(vault =>
        {
            vault
                .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
                .UseSoftDelete((IDeletedAt x) => x.DeletedAt)
                .UseSoftDelete((IRemovedAt x) => x.RemovedAt);
            configure?.Invoke(vault);
        }, services);

        await host.SeedAsync("Articles", new Article { Id = 1, Title = "first" }, new Article { Id = 2, Title = "second" });
        return host;
    }
}

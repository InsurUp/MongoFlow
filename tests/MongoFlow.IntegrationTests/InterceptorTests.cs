using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// <see cref="VaultInterceptor"/>s around a save:
/// <list type="number">
/// <item><c>SavingAsync</c> runs in registration order, defaults first; the hooks after the write run in reverse;</item>
/// <item>a failure at any step fails the save, writes nothing and runs every <c>FailedAsync</c>, whose own failures
/// don't hide the first;</item>
/// <item>during <c>SavingAsync</c> an interceptor can replace, remove or add writes, and the interceptors after it see
/// the result; afterwards it can't;</item>
/// <item>an interceptor sees only the operations of the collections it applies to, and not those queued with its
/// feature switched off;</item>
/// <item>one registered by type is created once per vault instance, from the scope's services;</item>
/// <item>the context carries the vault, the scope's services, the session, the result and items shared by every
/// hook.</item>
/// </list>
/// </summary>
public partial class InterceptorTests
{
    [ClassDataSource<MongoFixture>(Shared = SharedType.PerTestSession)]
    public required MongoFixture Mongo { get; init; }

    [Test]
    public async Task Hooks_SuccessfulSave_RunBeforeTheWriteInOrderAndAfterItInReverse()
    {
        // Arrange
        var log = new HookLog();
        await using var host = Mongo.Host<ShopVault>(
            vault => vault
                .AddInterceptor(new RecordingInterceptor("vault", log))
                .Collection(x => x.Orders, orders => orders.AddInterceptor(new RecordingInterceptor("orders", log)))
                .AddInterceptor<ScopedRecorder>(),
            services => services
                .AddSingleton(log)
                .AddDefaultVaultConfiguration(typeof(RecordingDefault<>)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Verify(log.Entries);
    }

    [Test]
    public async Task SavingAsync_Throws_FailsTheSaveAndRunsFailed()
    {
        // Arrange
        var log = new HookLog();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new RecordingInterceptor("first", log))
            .AddInterceptor(new ThrowingInterceptor(Hook.Saving))
            .AddInterceptor(new RecordingInterceptor("last", log)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InterceptorException>();

        // Assert
        await Verify(new { log.Entries, Stored = await host.StoredAsync("Orders") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task SavedAsync_Throws_RollsBackTheWrite()
    {
        // Arrange
        var log = new HookLog();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new RecordingInterceptor("first", log))
            .AddInterceptor(new ThrowingInterceptor(Hook.Saved)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await Assert.That(() => host.Vault.SaveAsync()).ThrowsExactly<InterceptorException>();

        // Assert
        await Verify(new { log.Entries, Stored = await host.StoredAsync("Orders") }).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task FailedAsync_Throws_LeavesTheOriginalFailureAndTheOtherHooks()
    {
        // Arrange
        var log = new HookLog();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new RecordingInterceptor("first", log))
            .AddInterceptor(new ThrowingInterceptor(Hook.Failed))
            .AddInterceptor(new ThrowingInterceptor(Hook.Saving)));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
        await Assert.That(log.Entries).IsEquivalentTo(["first.Saving", "first.Failed"]);
    }

    [Test]
    public async Task SaveAsync_FromTheVaultsOwnInterceptor_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(vault => vault.AddInterceptor(new SavingTheVault()));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act & Assert
        await ThrowsTask(() => host.Vault.SaveAsync()).IgnoreStackTrace();
    }

    [Test]
    public async Task Interceptor_RegisteredByType_IsCreatedOncePerVaultFromTheScope()
    {
        // Arrange
        await using var host = Mongo.Host<ShopVault>(
            vault => vault.AddInterceptor<InstanceRecorder>(),
            services => services
                .AddSingleton<CreatedInterceptors>()
                .AddScoped<RequestId>());
        await using var other = host.CreateScope();
        var otherVault = other.ServiceProvider.GetRequiredService<ShopVault>();

        // Act
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });
        await host.Vault.SaveAsync();
        host.Vault.Orders.Add(new Order { Id = 2, Customer = "bob", Total = 20 });
        await host.Vault.SaveAsync();
        otherVault.Orders.Add(new Order { Id = 3, Customer = "cy", Total = 30 });
        await otherVault.SaveAsync();

        // Assert — two instances, each with its own scope's request id.
        var created = host.Services.GetRequiredService<CreatedInterceptors>();
        await Assert.That(created.Instances).Count().IsEqualTo(2);
        await Assert.That(created.Instances[0].Request).IsSameReferenceAs(host.Services.GetRequiredService<RequestId>());
        await Assert.That(created.Instances[1].Request).IsSameReferenceAs(other.ServiceProvider.GetRequiredService<RequestId>());
    }

    [Test]
    public async Task Context_DuringASave_DescribesIt()
    {
        // Arrange
        var probe = new ContextProbe();
        await using var host = Mongo.Host<ShopVault>(vault => vault
            .AddInterceptor(new ItemWriter())
            .AddInterceptor(probe));
        host.Vault.Orders.Add(new Order { Id = 1, Customer = "ada", Total = 10 });

        // Act
        await host.Vault.SaveAsync();

        // Assert
        await Assert.That(probe.Vault).IsSameReferenceAs(host.Vault);
        await Assert.That(probe.Services).IsSameReferenceAs(host.Services);
        await Verify(new { probe.InTransaction, probe.ResultWhileSaving, probe.ResultOnceSaved, probe.Item });
    }
}

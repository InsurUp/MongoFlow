using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow.Tests;

public partial class VaultBuilderTests
{
    [Test]
    [MethodDataSource(nameof(NullArguments))]
    public async Task Configure_NullArgument_ThrowsArgumentNullException(string call,
        Action<IVaultBuilder<ShopVault>> configure)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(configure);

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    [MethodDataSource(nameof(InvalidArguments))]
    public async Task Configure_InvalidArgument_ThrowsArgumentException(string call,
        Action<IVaultBuilder<ShopVault>> configure)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(configure);

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task KeyedCollectionBuilder_EveryCall_ReturnsTheKeyedBuilder()
    {
        // Arrange
        const string Name = "chained_orders";
        await using var host = new VaultHost<ShopVault>(vault => vault.Collection(x => x.Orders, orders => orders
            .Name(Name)
            .QueryFilter(x => x.Total > 1)
            .QueryFilter(_ => x => x.Customer != "")
            .QueryFilter((_, _) => ValueTask.FromResult<Expression<Func<Order, bool>>>(x => x.TenantId != null))
            .Without(ActiveOnlyFeature.Key)
            .AddInterceptor<NoopInterceptor>()
            .AddInterceptor(new NoopInterceptor())
            .Key(x => x.Id)));

        // Act
        var orders = host.Vault.Orders;

        // Assert
        await Verify(new
        {
            orders.MongoCollection.CollectionNamespace.CollectionName,
            Query = (await orders.QueryAsync()).ToString()
        });
    }

    public static IEnumerable<Func<(string, Action<IVaultBuilder<ShopVault>>)>> NullArguments()
    {
        yield return () => ("UseDatabase(name)", vault => vault.UseDatabase((string)null!));
        yield return () => ("UseDatabase(database)", vault => vault.UseDatabase((IMongoDatabase)null!));
        yield return () => ("UseConfiguration", vault => vault.UseConfiguration(null!));
        yield return () => ("Collection(selector)", vault => vault.Collection<AuditEntry>(null!, _ => { }));
        yield return () => ("Collection(configure)", vault => vault.Collection(x => x.Audit, null!));
        yield return () => ("Collection<TKey>(selector)", vault => vault.Collection<Order, int>(null!, _ => { }));
        yield return () => ("Collection<TKey>(configure)", vault => vault.Collection(x => x.Orders, (Action<IVaultCollectionBuilder<Order, int>>)null!));
        yield return () => ("ForEachCollection", vault => vault.ForEachCollection(null!));
        yield return () => ("QueryFilter(static)", vault => vault.QueryFilter((Expression<Func<Order, bool>>)null!));
        yield return () => ("QueryFilter(per query)",
            vault => vault.QueryFilter((Func<IServiceProvider, Expression<Func<Order, bool>>>)null!));
        yield return () => ("QueryFilter(async)",
            vault => vault.QueryFilter((Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<Order, bool>>>>)null!));
        yield return () => ("AddInterceptor", vault => vault.AddInterceptor(null!));
        yield return () => ("AddInterceptor.For", vault => vault.AddInterceptor<NoopInterceptor>(interceptor => interceptor.For(null!)));
        yield return () => ("AddFeature", vault => vault.AddFeature((ActiveOnlyFeature)null!));
        yield return () => ("Collection.Name", vault => vault.Collection(x => x.Audit, audit => audit.Name(null!)));
        yield return () => ("Collection.QueryFilter(static)",
            vault => vault.Collection(x => x.Audit, audit => audit.QueryFilter((Expression<Func<AuditEntry, bool>>)null!)));
        yield return () => ("Collection.QueryFilter(per query)", vault => vault.Collection(x => x.Audit,
            audit => audit.QueryFilter((Func<IServiceProvider, Expression<Func<AuditEntry, bool>>>)null!)));
        yield return () => ("Collection.QueryFilter(async)", vault => vault.Collection(x => x.Audit, audit =>
            audit.QueryFilter((Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<AuditEntry, bool>>>>)null!)));
        yield return () => ("Collection.QueryFilter(lambda)",
            vault => vault.Collection(x => x.Audit, audit => audit.QueryFilter((LambdaExpression)null!)));
        yield return () => ("Collection.QueryFilter(lambda per query)", vault => vault.Collection(x => x.Audit,
            audit => audit.QueryFilter((Func<IServiceProvider, LambdaExpression>)null!)));
        yield return () => ("Collection.QueryFilter(lambda async)", vault => vault.Collection(x => x.Audit, audit =>
            audit.QueryFilter((Func<IServiceProvider, CancellationToken, ValueTask<LambdaExpression>>)null!)));
        yield return () => ("Collection.AddInterceptor", vault => vault.Collection(x => x.Audit, audit => audit.AddInterceptor(null!)));
        yield return () => ("KeyedCollection.Key", vault => vault.Collection(x => x.Orders, orders => orders.Key(null!)));
        yield return () => ("KeyedCollection.Name", vault => vault.Collection(x => x.Orders, orders => orders.Name(null!)));
        yield return () => ("KeyedCollection.QueryFilter(static)",
            vault => vault.Collection(x => x.Orders, orders => orders.QueryFilter((Expression<Func<Order, bool>>)null!)));
        yield return () => ("KeyedCollection.QueryFilter(per query)", vault => vault.Collection(x => x.Orders,
            orders => orders.QueryFilter((Func<IServiceProvider, Expression<Func<Order, bool>>>)null!)));
        yield return () => ("KeyedCollection.QueryFilter(async)", vault => vault.Collection(x => x.Orders, orders =>
            orders.QueryFilter((Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<Order, bool>>>>)null!)));
        yield return () => ("KeyedCollection.QueryFilter(lambda)",
            vault => vault.Collection(x => x.Orders, orders => orders.QueryFilter((LambdaExpression)null!)));
        yield return () => ("KeyedCollection.QueryFilter(lambda per query)", vault => vault.Collection(x => x.Orders,
            orders => orders.QueryFilter((Func<IServiceProvider, LambdaExpression>)null!)));
        yield return () => ("KeyedCollection.QueryFilter(lambda async)", vault => vault.Collection(x => x.Orders,
            orders => orders.QueryFilter((Func<IServiceProvider, CancellationToken, ValueTask<LambdaExpression>>)null!)));
        yield return () => ("KeyedCollection.AddInterceptor", vault => vault.Collection(x => x.Orders, orders => orders.AddInterceptor(null!)));
    }

    public static IEnumerable<Func<(string, Action<IVaultBuilder<ShopVault>>)>> InvalidArguments()
    {
        yield return () => ("UseDatabase(blank)", vault => vault.UseDatabase(" "));
        yield return () => ("Collection.Name(blank)", vault => vault.Collection(x => x.Audit, audit => audit.Name(" ")));
        yield return () => ("Collection.Without(default)", vault => vault.Collection(x => x.Audit, audit => audit.Without(default)));
        yield return () => ("KeyedCollection.Name(blank)", vault => vault.Collection(x => x.Orders, orders => orders.Name(" ")));
        yield return () => ("KeyedCollection.Without(default)", vault => vault.Collection(x => x.Orders, orders => orders.Without(default)));
    }
}

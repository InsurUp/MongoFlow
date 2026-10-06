using MongoDB.Bson;
using MongoDB.Driver;
using TUnit.Assertions.Enums;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="IVaultCollection"/> and <see cref="IKeyedVaultCollection"/>, a vault's collections without their type
/// arguments, before anything reaches the server:
/// <list type="number">
/// <item>the vault finds them by document type, and a keyed one only when the collection has a key;</item>
/// <item>each write queues what the typed call queues, so what an interceptor reads of an operation queues it again
/// unchanged;</item>
/// <item>their views switch features off, and a keyed one stays keyed;</item>
/// <item>documents, keys and updates the compiler would have rejected are rejected when the write is queued, and nothing
/// is queued.</item>
/// </list>
/// </summary>
public partial class UntypedCollectionTests
{
    [Test]
    [Arguments(typeof(Order))]
    [Arguments(typeof(AuditEntry))]
    public async Task Collection_DeclaredDocument_DescribesTheVaultsCollection(Type documentType)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var collection = host.Vault.Collection(documentType);

        // Assert
        await Verify(new { collection.DocumentType, collection.KeyType, collection.PropertyName });
    }

    [Test]
    public async Task KeyedCollection_KeyedDocument_DescribesTheVaultsCollection()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act
        var collection = host.Vault.KeyedCollection(typeof(Order));

        // Assert
        await Verify(new { collection.DocumentType, collection.KeyType, collection.PropertyName });
    }

    [Test]
    public async Task Collection_UndeclaredDocument_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Throws(() => host.Vault.Collection(typeof(Uri))).IgnoreStackTrace();
    }

    [Test]
    public async Task KeyedCollection_UndeclaredDocument_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => host.Vault.KeyedCollection(typeof(Uri))).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task KeyedCollection_KeylessCollection_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Throws(() => host.Vault.KeyedCollection(typeof(AuditEntry))).IgnoreStackTrace();
    }

    [Test]
    public async Task Writes_QueuedAgainFromTheirOperations_QueueWhatTheTypedCallsQueued()
    {
        // Arrange — every kind of write, typed, on one vault. Its operations are queued again on another, untyped, from
        // what an interceptor for every collection reads of them.
        await using var typed = new VaultHost<ShopVault>();
        await using var untyped = new VaultHost<ShopVault>();
        var order = new Order { Id = 1, Customer = "ada", Total = 10 };
        typed.Vault.Orders.Add(order);
        typed.Vault.Audit.Add(new AuditEntry { Message = "created" });
        typed.Vault.Orders.Replace(order);
        typed.Vault.Orders.Update(order, Builders<Order>.Update.Inc(x => x.Total, 1));
        typed.Vault.Orders.UpdateByKey(2, Builders<Order>.Update.Set(x => x.Customer, "bob"));
        typed.Vault.Orders.UpdateMany(x => x.Total > 5,
            Builders<Order>.Update.Pipeline(new[] { new BsonDocument("$set", new BsonDocument("Customer", "many")) }));
        typed.Vault.Audit.UpdateMany(x => x.Message == "created", Builders<AuditEntry>.Update.Set(x => x.Message, "seen"));
        typed.Vault.Orders.Delete(order);
        typed.Vault.Orders.DeleteByKey(3);
        typed.Vault.Audit.DeleteMany(x => x.Message == "seen");
        var queued = Drain(typed.Vault);

        // Act
        foreach (var operation in queued)
        {
            QueueAgain(untyped.Vault, operation);
        }

        // Assert
        await Assert.That(Describe(Drain(untyped.Vault))).IsEquivalentTo(Describe(queued), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Without_UntypedViews_QueueWithTheFeatureOff()
    {
        // Arrange
        var audit = new FeatureKey("audit");
        await using var host = new VaultHost<ShopVault>();

        // Act
        host.Vault.Collection(typeof(AuditEntry)).Without(audit).Add(new AuditEntry());
        host.Vault.KeyedCollection(typeof(Order)).Without(audit).WithNoTracking().DeleteByKey(1);

        // Assert
        await Assert.That(Drain(host.Vault).Select(operation => operation.DisabledFeatures.Contains(audit)))
            .IsEquivalentTo([true, true]);
    }

    [Test]
    public async Task Add_DocumentOfADerivedType_QueuesIt()
    {
        // Arrange
        await using var host = new VaultHost<FleetVault>();
        var truck = new Truck { Id = 1 };

        // Act
        host.Vault.Collection(typeof(Vehicle)).Add(truck);

        // Assert
        await Assert.That(Drain(host.Vault).Single().Document).IsSameReferenceAs(truck);
    }

    [Test]
    public async Task Add_AnotherDocumentType_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Throws(() => host.Vault.Collection(typeof(Order)).Add(new AuditEntry())).IgnoreStackTrace();
    }

    [Test]
    [MethodDataSource(nameof(WritesOfAnotherDocumentType))]
    public async Task Write_AnotherDocumentType_ThrowsArgumentExceptionAndQueuesNothing(string call,
        Action<ShopVault> write)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => write(host.Vault)).ThrowsExactly<ArgumentException>();
        await Assert.That(host.Vault.Runtime.Drain()).IsNull();
    }

    [Test]
    public async Task DeleteByKey_AnotherKeyType_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Throws(() => host.Vault.KeyedCollection(typeof(Order)).DeleteByKey(1L)).IgnoreStackTrace();
    }

    [Test]
    [MethodDataSource(nameof(WritesByAnotherKeyType))]
    public async Task Write_AnotherKeyType_ThrowsArgumentExceptionAndQueuesNothing(string call,
        Action<ShopVault> write)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => write(host.Vault)).ThrowsExactly<ArgumentException>();
        await Assert.That(host.Vault.Runtime.Drain()).IsNull();
    }

    [Test]
    public async Task GetByKeyAsync_AnotherKeyType_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(async () => await host.Vault.KeyedCollection(typeof(Order)).GetByKeyAsync("1"))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task UpdateMany_Replacement_ThrowsArgumentException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Throws(() => host.Vault.Collection(typeof(Order)).UpdateMany([], new BsonDocument("Total", 20)))
            .IgnoreStackTrace();
    }

    [Test]
    [Arguments("{ }")]
    [Arguments("{ Total: 20 }")]
    [Arguments("{ $set: { Total: 20 }, Customer: 'ada' }")]
    [Arguments("[]")]
    [Arguments("[{ Total: 20 }]")]
    [Arguments("[{ $set: { Total: 20 }, $unset: 'Customer' }]")]
    [Arguments("[20]")]
    [Arguments("20")]
    public async Task UpdateMany_NotAnUpdate_ThrowsArgumentExceptionAndQueuesNothing(string update)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => host.Vault.Collection(typeof(Order)).UpdateMany([], Bson(update)))
            .ThrowsExactly<ArgumentException>();
        await Assert.That(host.Vault.Runtime.Drain()).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(WritesOfAReplacement))]
    public async Task Write_Replacement_ThrowsArgumentExceptionAndQueuesNothing(string call,
        Action<ShopVault> write)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => write(host.Vault)).ThrowsExactly<ArgumentException>();
        await Assert.That(host.Vault.Runtime.Drain()).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(NullArguments))]
    public async Task Call_NullArgument_ThrowsArgumentNullException(string call,
        Action<ShopVault> act)
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(() => act(host.Vault)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task GetByKeyAsync_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>();

        // Act & Assert
        await Assert.That(async () => await host.Vault.KeyedCollection(typeof(Order)).GetByKeyAsync(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    public static IEnumerable<Func<(string, Action<ShopVault>)>> WritesOfAnotherDocumentType()
    {
        var entry = new AuditEntry();
        var update = new BsonDocument("$set", new BsonDocument("Total", 20));

        yield return () => ("Add", vault => vault.Collection(typeof(Order)).Add(entry));
        yield return () => ("AddRange", vault => vault.Collection(typeof(Order)).AddRange([entry]));
        yield return () => ("Replace", vault => vault.KeyedCollection(typeof(Order)).Replace(entry));
        yield return () => ("Delete", vault => vault.KeyedCollection(typeof(Order)).Delete(entry));
        yield return () => ("Update", vault => vault.KeyedCollection(typeof(Order)).Update(entry, update));
    }

    public static IEnumerable<Func<(string, Action<ShopVault>)>> WritesByAnotherKeyType()
    {
        var update = new BsonDocument("$set", new BsonDocument("Total", 20));

        yield return () => ("DeleteByKey", vault => vault.KeyedCollection(typeof(Order)).DeleteByKey("1"));
        yield return () => ("UpdateByKey", vault => vault.KeyedCollection(typeof(Order)).UpdateByKey("1", update));
    }

    public static IEnumerable<Func<(string, Action<ShopVault>)>> WritesOfAReplacement()
    {
        var replacement = new BsonDocument("Total", 20);

        yield return () => ("UpdateByKey", vault => vault.KeyedCollection(typeof(Order)).UpdateByKey(1, replacement));
        yield return () => ("Update", vault => vault.KeyedCollection(typeof(Order)).Update(new Order { Id = 1 }, replacement));
    }

    public static IEnumerable<Func<(string, Action<ShopVault>)>> NullArguments()
    {
        var update = new BsonDocument("$set", new BsonDocument("Total", 20));

        yield return () => ("Collection", vault => vault.Collection(null!));
        yield return () => ("KeyedCollection", vault => vault.KeyedCollection(null!));
        yield return () => ("Add", vault => vault.Collection(typeof(AuditEntry)).Add(null!));
        yield return () => ("AddRange", vault => vault.Collection(typeof(AuditEntry)).AddRange(null!));
        yield return () => ("AddRange(document)", vault => vault.Collection(typeof(AuditEntry)).AddRange([null!]));
        yield return () => ("UpdateMany(filter)", vault => vault.Collection(typeof(Order)).UpdateMany(null!, update));
        yield return () => ("UpdateMany(update)", vault => vault.Collection(typeof(Order)).UpdateMany([], null!));
        yield return () => ("DeleteMany", vault => vault.Collection(typeof(AuditEntry)).DeleteMany(null!));
        yield return () => ("Replace", vault => vault.KeyedCollection(typeof(Order)).Replace(null!));
        yield return () => ("Delete", vault => vault.KeyedCollection(typeof(Order)).Delete(null!));
        yield return () => ("DeleteByKey", vault => vault.KeyedCollection(typeof(Order)).DeleteByKey(null!));
        yield return () => ("UpdateByKey(key)", vault => vault.KeyedCollection(typeof(Order)).UpdateByKey(null!, update));
        yield return () => ("UpdateByKey(update)", vault => vault.KeyedCollection(typeof(Order)).UpdateByKey(1, null!));
        yield return () => ("Update(document)", vault => vault.KeyedCollection(typeof(Order)).Update(null!, update));
        yield return () => ("Update(update)", vault => vault.KeyedCollection(typeof(Order)).Update(new Order(), null!));
    }

    private static VaultOperation[] Drain(MongoVault vault)
    {
        using var queued = vault.Runtime.Drain()!;

        return queued.Span.ToArray();
    }

    /// <summary>Queues <paramref name="operation"/> again, untyped, from what an interceptor reads of it.</summary>
    private static void QueueAgain(IMongoVault vault,
        VaultOperation operation)
    {
        var type = operation.Collection.DocumentType;

        switch (operation)
        {
            case { Kind: OperationKind.Insert }:
                vault.Collection(type).Add(operation.Document!);
                break;
            case { Kind: OperationKind.Replace }:
                vault.KeyedCollection(type).Replace(operation.Document!);
                break;
            case { Kind: OperationKind.Update, IsSetBased: true }:
                vault.Collection(type).UpdateMany(operation.RenderFilter()!, operation.RenderUpdate()!);
                break;
            case { Kind: OperationKind.Update, Document: { } document }:
                vault.KeyedCollection(type).Update(document, operation.RenderUpdate()!);
                break;
            case { Kind: OperationKind.Update }:
                vault.KeyedCollection(type).UpdateByKey(operation.Key!, operation.RenderUpdate()!);
                break;
            case { IsSetBased: true }:
                vault.Collection(type).DeleteMany(operation.RenderFilter()!);
                break;
            case { Document: { } document }:
                vault.KeyedCollection(type).Delete(document);
                break;
            default:
                vault.KeyedCollection(type).DeleteByKey(operation.Key!);
                break;
        }
    }

    /// <summary>
    /// What anything downstream of the queue can tell about each operation, with BSON as JSON, which the equivalence
    /// check compares as text rather than member by member.
    /// </summary>
    private static object[] Describe(IEnumerable<VaultOperation> operations) =>
    [
        .. operations.Select(operation => new
        {
            Type = operation.GetType().Name,
            operation.Kind,
            operation.Collection.PropertyName,
            operation.Document,
            operation.Key,
            operation.IsSetBased,
            Filter = operation.RenderFilter()?.ToString(),
            Update = operation.RenderUpdate()?.ToString(),
            operation.DisabledFeatures.IsEmpty
        })
    ];

    private static BsonValue Bson(string json) => BsonDocument.Parse($"{{ value: {json} }}")["value"];
}

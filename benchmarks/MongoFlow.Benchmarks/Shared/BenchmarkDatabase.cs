using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// A database of its own on the benchmark server, for one benchmark case: created with its orders collection, so no
/// save creates it inside a transaction, and dropped when the case ends.
/// </summary>
public sealed class BenchmarkDatabase : IDisposable
{
    public BenchmarkDatabase()
    {
        Client = new MongoClient(BenchmarkServer.ConnectionString);
        Database = Client.GetDatabase($"benchmarks_{Guid.NewGuid():N}");
        Database.CreateCollection("Orders");
        Orders = Database.GetCollection<Order>("Orders");
    }

    public MongoClient Client { get; }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Order> Orders { get; }

    public void Seed(int count) =>
        Orders.InsertMany(Enumerable.Range(1, count).Select(id => new Order
        {
            Id = id,
            Customer = "ada",
            Total = id,
            TenantId = Tenant.Current
        }));

    public void Dispose()
    {
        Client.DropDatabase(Database.DatabaseNamespace.DatabaseName);
        Client.Dispose();
    }
}

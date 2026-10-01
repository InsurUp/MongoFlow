using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>What an app without MongoFlow resolves per request: a repository over the driver's collections.</summary>
public sealed class OrderRepository(IMongoDatabase database)
{
    public IMongoCollection<Order> Orders { get; } = database.GetCollection<Order>("Orders");

    public IMongoCollection<AuditEntry> Audit { get; } = database.GetCollection<AuditEntry>("Audit");
}

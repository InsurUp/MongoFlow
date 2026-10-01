using BenchmarkDotNet.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>
/// What every write by key pays for its filter: MongoFlow's key model building it as BSON, against the driver rendering
/// a LINQ filter (what a filter expression per write costs) and an equality filter on the key's members.
/// </summary>
[MemoryDiagnoser]
public class KeyFilterBenchmarks
{
    private readonly int _id = 42;
    private readonly TokenKey _token = new("user-1", "github");

    private KeyModel<Order, int> _idKey = null!;
    private KeyModel<LoginToken, TokenKey> _compositeKey = null!;
    private RenderArgs<Order> _orderArgs;
    private RenderArgs<LoginToken> _tokenArgs;

    [ParamsAllValues]
    public KeyShape Key { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var database = Vaults.Offline.GetDatabase("benchmarks");
        var orders = database.GetCollection<Order>("Orders");
        var tokens = database.GetCollection<LoginToken>("Tokens");

        _idKey = KeyModel<Order, int>.Create(null, orders);
        _compositeKey = KeyModel<LoginToken, TokenKey>.Create(t => new TokenKey(t.UserId, t.Provider), tokens);
        _orderArgs = FilterDocuments.RenderArgs(orders);
        _tokenArgs = FilterDocuments.RenderArgs(tokens);
    }

    [Benchmark(Baseline = true, Description = "Driver: LINQ filter")]
    public BsonDocument DriverLinq() =>
        Key == KeyShape.Id
            ? Builders<Order>.Filter.Where(x => x.Id == _id).Render(_orderArgs)
            : Builders<LoginToken>.Filter.Where(x => x.UserId == _token.UserId && x.Provider == _token.Provider).Render(_tokenArgs);

    [Benchmark(Description = "Driver: Eq filter")]
    public BsonDocument DriverEq() =>
        Key == KeyShape.Id
            ? Builders<Order>.Filter.Eq(x => x.Id, _id).Render(_orderArgs)
            : (Builders<LoginToken>.Filter.Eq(x => x.UserId, _token.UserId) &
               Builders<LoginToken>.Filter.Eq(x => x.Provider, _token.Provider)).Render(_tokenArgs);

    [Benchmark(Description = "MongoFlow: key model")]
    public BsonDocument MongoFlow() => Key == KeyShape.Id ? _idKey.Match(_id) : _compositeKey.Match(_token);
}

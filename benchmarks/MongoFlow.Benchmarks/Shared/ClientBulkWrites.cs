using MongoDB.Driver;

namespace MongoFlow.Benchmarks;

/// <summary>The driver doing what a save does: one ordered, verbose client bulk write, in a transaction of its own.</summary>
public static class ClientBulkWrites
{
    private static readonly ClientBulkWriteOptions Options = new() { IsOrdered = true, VerboseResult = true };

    public static async Task<ClientBulkWriteResult> WriteAsync(IMongoClient client,
        IReadOnlyList<BulkWriteModel> models)
    {
        using var session = await client.StartSessionAsync();
        session.StartTransaction();

        var result = await client.BulkWriteAsync(session, models, Options);
        await session.CommitTransactionAsync();

        return result;
    }
}

using MongoDB.Bson;

namespace MongoFlow.Tests.Search;

public partial class CollectionSearchTests
{
    /// <summary>Stands in for <c>$listSearchIndexes</c>: lists one index, counting the calls, once let through.</summary>
    private sealed class Lister(BsonDocument? listed)
    {
        private int _calls;

        public int Calls => _calls;

        public TaskCompletionSource? Gate { get; init; }

        public Exception? FailFirst { get; init; }

        public async Task<BsonDocument?> ListAsync(string name,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (Gate is not null)
            {
                await Gate.Task;
            }

            return call == 1 && FailFirst is not null ? throw FailFirst : listed;
        }
    }
}

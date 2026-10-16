using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;

namespace MongoFlow.Tests.Search;

public partial class SearchIndexSyncTests
{
    /// <summary>Stands in for time while the waiter waits: each wait is recorded, and over at once.</summary>
    private sealed class InstantTime : TimeProvider
    {
        private readonly List<TimeSpan> _waits = [];

        public IReadOnlyList<TimeSpan> Waits
        {
            get
            {
                lock (_waits)
                {
                    return [.. _waits];
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            lock (_waits)
            {
                _waits.Add(dueTime);
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new Elapsed();
        }

        private sealed class Elapsed : ITimer
        {
            public bool Change(TimeSpan dueTime,
                TimeSpan period) =>
                false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static MongoCommandException CommandFailed(int code) =>
        new(new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))),
            $"Command failed with code {code}.",
            [],
            new BsonDocument { { "ok", 0 }, { "code", code } });
}

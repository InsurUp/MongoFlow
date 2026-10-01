using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Prest;

namespace MongoFlow;

/// <summary>One save in progress: the shared operation list and what the steps produce.</summary>
/// <remarks>
/// It owns the pooled operation list it's handed, and gives it back when disposed: by the transaction it joined when that
/// ends, or by the save itself when it never joined one. Every hook the save can run has run by then.
/// </remarks>
internal sealed class SaveRun(VaultRuntime runtime, PooledList<VaultOperation> operations) : IDisposable
{
    // The driver copies what it needs out of the options, so one instance serves every save.
    private static readonly ClientBulkWriteOptions WriteOptions = new() { IsOrdered = true, VerboseResult = true };

    private readonly Dictionary<(IVaultCollectionInfo, FeatureSet), BsonDocument?> _queryFilters = [];
    private bool _saving;

    // Interceptors mostly replace or remove operations in queue order, so each search starts where the last one ended:
    // a pass over the save is linear rather than quadratic.
    private int _searchFrom;
    private bool _disposed;

    public VaultRuntime Runtime { get; } = runtime;

    public PooledList<VaultOperation> Operations { get; } = operations;

    public IClientSessionHandle Session { get; set; } = null!;

    public SaveResult? Result { get; private set; }

    public Dictionary<object, object?> Items { get; } = [];

    public async Task SavingAsync(CancellationToken cancellationToken)
    {
        _saving = true;
        try
        {
            for (var i = 0; i < Runtime.Model.Interceptors.Count; i++)
            {
                await Runtime.GetInterceptor(i).SavingAsync(ContextFor(i), cancellationToken);

                // Writes an interceptor queued on the vault join the save, for the interceptors after it to see.
                Runtime.DrainInto(Operations);
            }
        }
        finally
        {
            _saving = false;
        }
    }

    public async Task<SaveResult> WriteAsync(CancellationToken cancellationToken)
    {
        if (Operations.Count == 0)
        {
            Result = SaveResult.Empty;
            return SaveResult.Empty;
        }

        var log = Runtime.Model.Logs.Save;
        var tracing = log.IsEnabled(LogLevel.Trace);

        using var models = new PooledList<BulkWriteModel>(Operations.Count, clearOnReturn: true);
        foreach (var operation in Operations)
        {
            if (tracing)
            {
                log.Writing(operation.Kind, operation.Namespace.CollectionName, operation.TargetKey, operation.IsSetBased);
            }

            models.Add(await operation.CreateWriteModelAsync(this, cancellationToken));
        }

        var written = await Runtime.Model.Client.BulkWriteAsync(Session, models, WriteOptions, cancellationToken);

        for (var i = 0; i < Operations.Count; i++)
        {
            Operations[i].Result = ResultOf(Operations[i], written, i);
        }

        var result = new SaveResult(written.InsertedCount, written.MatchedCount, written.ModifiedCount,
            written.DeletedCount);
        Result = result;

        return result;
    }

    // The hooks after the write run in reverse, so the interceptor that ran last before it, such as the concurrency
    // token's, runs first after it.
    public async Task SavedAsync(CancellationToken cancellationToken)
    {
        for (var i = Runtime.Model.Interceptors.Count - 1; i >= 0; i--)
        {
            await Runtime.GetInterceptor(i).SavedAsync(ContextFor(i), cancellationToken);
        }
    }

    public async Task CommittedAsync(CancellationToken cancellationToken)
    {
        for (var i = Runtime.Model.Interceptors.Count - 1; i >= 0; i--)
        {
            await Runtime.GetInterceptor(i).CommittedAsync(ContextFor(i), cancellationToken);
        }
    }

    /// <summary>
    /// Runs every interceptor's failure hook. Called whenever the save's writes are rolled back: by its own failure, or by
    /// the transaction it joined. Hook exceptions are ignored so the original failure surfaces.
    /// </summary>
    public async Task FailedAsync(Exception exception, CancellationToken cancellationToken)
    {
        for (var i = Runtime.Model.Interceptors.Count - 1; i >= 0; i--)
        {
            var interceptor = Runtime.GetInterceptor(i);
            try
            {
                await interceptor.FailedAsync(ContextFor(i), exception, cancellationToken);
            }
            catch (Exception hookException)
            {
                // Ignored, see above, but logged: nothing else would show it.
                Runtime.Model.Logs.Save.FailureHookThrew(interceptor.GetType().Name, Runtime.Model.VaultType.Name, hookException);
            }
        }
    }

    /// <summary>
    /// A collection's query filters for operations queued with <paramref name="disabled"/> features, resolved and
    /// rendered once per save.
    /// </summary>
    public async ValueTask<BsonDocument?> QueryFilterAsync<TDocument>(CollectionModel<TDocument> collection,
        FeatureSet disabled, CancellationToken cancellationToken)
    {
        (IVaultCollectionInfo, FeatureSet) key = (collection, disabled);
        if (_queryFilters.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var filter = await collection.ResolveFilterAsync(Runtime.Services, disabled, cancellationToken);
        var rendered = filter is null ? null : collection.Render(filter);
        _queryFilters[key] = rendered;

        return rendered;
    }

    public void Replace(VaultOperation operation, VaultOperation replacement)
    {
        ThrowIfNotSaving(nameof(Replace));
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(replacement);

        if (!ReferenceEquals(operation.Collection, replacement.Collection))
        {
            throw new ArgumentException("The replacement targets a different collection.", nameof(replacement));
        }

        Operations[IndexOf(operation)] = replacement;
    }

    public void Remove(VaultOperation operation)
    {
        ThrowIfNotSaving(nameof(Remove));
        ArgumentNullException.ThrowIfNull(operation);

        Operations.RemoveAt(IndexOf(operation));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Operations.Dispose();
        }
    }

    private SaveContext ContextFor(int interceptor) => new(this, Runtime.Model.Interceptors[interceptor]);

    private int IndexOf(VaultOperation operation)
    {
        var count = Operations.Count;
        var start = Math.Min(_searchFrom, count);

        for (var searched = 0; searched < count; searched++)
        {
            var i = start + searched < count ? start + searched : start + searched - count;
            if (ReferenceEquals(Operations[i], operation))
            {
                return _searchFrom = i;
            }
        }

        throw new ArgumentException("The operation isn't part of this save.", nameof(operation));
    }

    private void ThrowIfNotSaving(string method)
    {
        if (!_saving)
        {
            throw new InvalidOperationException(
                $"{method} can only be called during {nameof(VaultInterceptor.SavingAsync)}.");
        }
    }

    // A verbose bulk write reports every operation it ran; replaces are reported with the updates.
    private static OperationResult ResultOf(VaultOperation operation,
        ClientBulkWriteResult written,
        int index)
    {
        switch (operation.Kind)
        {
            case OperationKind.Insert:
                return OperationResult.Of(1, 0, 0, 0);

            case OperationKind.Delete:
                return OperationResult.Of(0, 0, 0, written.DeleteResults[index].DeletedCount);

            default:
                var updated = written.UpdateResults[index];
                return OperationResult.Of(0, updated.MatchedCount, updated.ModifiedCount, 0);
        }
    }
}
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
internal sealed class SaveRun(VaultRuntime runtime,
    PooledList<VaultOperation> operations,
    TrackedChanges? tracked) : IDisposable
{
    // The driver copies what it needs out of the options, so one instance serves every save.
    private static readonly ClientBulkWriteOptions WriteOptions = new() { IsOrdered = true, VerboseResult = true };

    // The save whose interceptors are running in this flow, set while their hooks run. What they queue joins their save,
    // and saves they make join its transaction; other tasks' writes wait for the next save, and their saves fail.
    private static readonly AsyncLocal<SaveRun?> Running = new();

    // Writes the save's own interceptors queued during SavingAsync, for the interceptors after them to see.
    private readonly Lock _addedLock = new();
    private PooledList<VaultOperation>? _added;

    private readonly Dictionary<(IVaultCollectionInfo, FeatureSet), BsonDocument?> _queryFilters = [];
    private bool _saving;

    // Interceptors mostly replace or remove operations in queue order, so each search starts where the last one ended:
    // a pass over the save is linear rather than quadratic.
    private int _searchFrom;
    private bool _disposed;

    // What each interceptor sees of the operations, kept until they change, so reading them again copies nothing.
    private (int Changes, IReadOnlyList<VaultOperation>? Operations)[]? _seen;
    private int _changes;

    public VaultRuntime Runtime { get; } = runtime;

    public PooledList<VaultOperation> Operations { get; } = operations;

    public IClientSessionHandle Session { get; set; } = null!;

    public SaveResult? Result { get; private set; }

    public Dictionary<object, object?> Items { get; } = [];

    /// <summary>The save whose interceptors are running in the current flow, if any.</summary>
    public static SaveRun? Current => Running.Value;

    /// <summary>The transaction the save runs in: its own, or the one it joined.</summary>
    public VaultTransaction Transaction { get; init; } = null!;

    /// <summary>Whether its interceptors' <see cref="VaultInterceptor.SavingAsync"/> are running, when they can add writes.</summary>
    public bool IsSaving => _saving;

    /// <summary>Queues a write one of the save's interceptors made, to join the save after that interceptor.</summary>
    public void Add(VaultOperation operation)
    {
        lock (_addedLock)
        {
            (_added ??= new PooledList<VaultOperation>(clearOnReturn: true)).Add(operation);
        }
    }

    public async Task SavingAsync(CancellationToken cancellationToken)
    {
        // Without interceptors there's no hook to run, nor flow to mark.
        if (Runtime.Model.Interceptors.Count == 0)
        {
            return;
        }

        _saving = true;
        Running.Value = this;
        try
        {
            for (var i = 0; i < Runtime.Model.Interceptors.Count; i++)
            {
                await Runtime.GetInterceptor(i).SavingAsync(ContextFor(i), cancellationToken);

                // Writes an interceptor queued on the vault join the save, for the interceptors after it to see.
                if (TakeAdded())
                {
                    _changes++;
                }
            }
        }
        finally
        {
            _saving = false;
            Running.Value = null;
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
        if (Runtime.Model.Interceptors.Count == 0)
        {
            return;
        }

        Running.Value = this;
        try
        {
            for (var i = Runtime.Model.Interceptors.Count - 1; i >= 0; i--)
            {
                await Runtime.GetInterceptor(i).SavedAsync(ContextFor(i), cancellationToken);
            }
        }
        finally
        {
            Running.Value = null;
        }
    }

    public async Task CommittedAsync(CancellationToken cancellationToken)
    {
        // Counted on commit, so writes rolled back with their transaction never are.
        Runtime.Model.Metrics.RecordCommitted(Runtime.Model.VaultType.Name, Operations.Span);
        tracked?.Commit();

        if (Runtime.Model.Interceptors.Count == 0)
        {
            return;
        }

        Running.Value = this;
        try
        {
            for (var i = Runtime.Model.Interceptors.Count - 1; i >= 0; i--)
            {
                var interceptor = Runtime.GetInterceptor(i);
                try
                {
                    await interceptor.CommittedAsync(ContextFor(i), cancellationToken);
                }
                catch (Exception hookException)
                {
                    // The writes are committed, so the save succeeded: logged, since nothing else would show it.
                    Runtime.Model.Logs.Save.CommittedHookThrew(interceptor.GetType().Name, Runtime.Model.VaultType.Name, hookException);
                }
            }
        }
        finally
        {
            Running.Value = null;

            // Its last hooks have run; without interceptors, nothing can read the originals, and its disposal ends it.
            tracked?.End();
        }
    }

    /// <summary>
    /// Runs every interceptor's failure hook. Called whenever the save's writes are rolled back: by its own failure, or by
    /// the transaction it joined. Hook exceptions are ignored so the original failure surfaces.
    /// </summary>
    public async Task FailedAsync(Exception exception, CancellationToken cancellationToken)
    {
        // The changes it wrote to tracked documents are pending again.
        tracked?.Revert();

        if (Runtime.Model.Interceptors.Count == 0)
        {
            return;
        }

        Running.Value = this;
        try
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
        finally
        {
            Running.Value = null;
            tracked?.End();
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
        _changes++;
    }

    public void Remove(VaultOperation operation)
    {
        ThrowIfNotSaving(nameof(Remove));
        ArgumentNullException.ThrowIfNull(operation);

        Operations.RemoveAt(IndexOf(operation));
        _changes++;
    }

    /// <summary>The operations <paramref name="interceptor"/> sees, in queue order.</summary>
    public IReadOnlyList<VaultOperation> OperationsSeenBy(int interceptor)
    {
        _seen ??= new (int, IReadOnlyList<VaultOperation>?)[Runtime.Model.Interceptors.Count];
        ref var seen = ref _seen[interceptor];

        if (seen.Operations is null || seen.Changes != _changes)
        {
            var model = Runtime.Model.Interceptors[interceptor];
            var operations = new List<VaultOperation>(Operations.Count);

            foreach (var operation in Operations)
            {
                if (model.Sees(operation))
                {
                    operations.Add(operation);
                }
            }

            seen = (_changes, operations.AsReadOnly());
        }

        return seen.Operations;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            tracked?.End();
            Operations.Dispose();
            TakeAdded();
        }
    }

    private SaveContext ContextFor(int interceptor) => new(this, interceptor);

    /// <summary>Moves the writes the save's interceptors queued into the save; returns whether there were any.</summary>
    private bool TakeAdded()
    {
        lock (_addedLock)
        {
            if (_added is not { } added)
            {
                return false;
            }

            _added = null;
            if (!_disposed)
            {
                Operations.AddRange(added.Span);
            }

            added.Dispose();
            return true;
        }
    }

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
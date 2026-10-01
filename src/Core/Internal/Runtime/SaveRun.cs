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

    private readonly Dictionary<(IVaultCollectionInfo, string), BsonDocument?> _queryFilters = [];
    private bool _saving;
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

        using var models = new PooledList<BulkWriteModel>(Operations.Count, clearOnReturn: true);
        foreach (var t in Operations)
        {
            models.Add(await t.CreateWriteModelAsync(this, cancellationToken));
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
            try
            {
                await Runtime.GetInterceptor(i).FailedAsync(ContextFor(i), exception, cancellationToken);
            }
            catch
            {
                // Ignored, see above.
            }
        }
    }

    /// <summary>
    /// A collection's query filters for operations queued with <paramref name="disabled"/> features, resolved and
    /// rendered once per save.
    /// </summary>
    public async ValueTask<BsonDocument?> QueryFilterAsync<TDocument>(CollectionModel<TDocument> collection,
        IReadOnlySet<FeatureKey> disabled, CancellationToken cancellationToken)
    {
        var key = (collection, string.Join(',', disabled.Select(feature => feature.Name).Order()));
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

    private SaveContext ContextFor(int interceptor) =>
        new InterceptorSaveContext(this, Runtime.Model.Interceptors[interceptor]);

    private int IndexOf(VaultOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var i = 0; i < Operations.Count; i++)
        {
            if (ReferenceEquals(Operations[i], operation))
            {
                return i;
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

    private static OperationResult ResultOf(VaultOperation operation, ClientBulkWriteResult written, int index) =>
        operation.Kind switch
        {
            OperationKind.Insert => OperationResult.Of(1, 0, 0, 0),
            OperationKind.Delete => written.DeleteResults.TryGetValue(index, out var deleted)
                ? OperationResult.Of(0, 0, 0, deleted.DeletedCount)
                : OperationResult.Of(0, 0, 0, 0),
            _ => written.UpdateResults.TryGetValue(index, out var updated)
                ? OperationResult.Of(0, updated.MatchedCount, updated.ModifiedCount, 0)
                : OperationResult.Of(0, 0, 0, 0)
        };
}
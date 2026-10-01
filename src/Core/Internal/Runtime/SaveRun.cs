using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>One save in progress: the shared operation list and what the steps produce.</summary>
internal sealed class SaveRun(VaultRuntime runtime, List<VaultOperation> operations)
{
    private readonly Dictionary<(IVaultCollectionInfo, string), object?> _queryFilters = [];
    private bool _saving;

    public VaultRuntime Runtime { get; } = runtime;

    public List<VaultOperation> Operations { get; } = operations;

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
                Operations.AddRange(Runtime.Drain());
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
            return Result = SaveResult.Empty;
        }

        var models = new List<BulkWriteModel>(Operations.Count);
        foreach (var operation in Operations)
        {
            models.Add(await operation.CreateWriteModelAsync(this, cancellationToken));
        }

        var written = await Runtime.Model.Client.BulkWriteAsync(Session, models,
            new ClientBulkWriteOptions { IsOrdered = true, VerboseResult = true }, cancellationToken);

        for (var i = 0; i < Operations.Count; i++)
        {
            Operations[i].Result = ResultOf(Operations[i], written, i);
        }

        return Result = new SaveResult(written.InsertedCount, written.MatchedCount, written.ModifiedCount, written.DeletedCount);
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
    /// A collection's query filters for operations queued with <paramref name="disabled"/> features, resolved once per
    /// save. The cache holds collections of different document types, so entries are stored untyped.
    /// </summary>
    public async ValueTask<Expression<Func<TDocument, bool>>?> QueryFilterAsync<TDocument>(CollectionModel<TDocument> collection,
        IReadOnlySet<FeatureKey> disabled, CancellationToken cancellationToken)
    {
        var key = (collection, string.Join(',', disabled.Select(feature => feature.Name).Order()));
        if (_queryFilters.TryGetValue(key, out var cached))
        {
            return (Expression<Func<TDocument, bool>>?)cached;
        }

        var filter = await collection.ResolveFilterAsync(Runtime.Services, disabled, cancellationToken);
        _queryFilters[key] = filter;

        return filter;
    }

    public bool Sees(InterceptorModel interceptor, VaultOperation operation) =>
        interceptor.AppliesTo(operation.Collection) &&
        (interceptor.Owner is not { } owner || !operation.DisabledFeatures.Contains(owner));

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

    private SaveContext ContextFor(int interceptor) => new InterceptorSaveContext(this, Runtime.Model.Interceptors[interceptor]);

    private int IndexOf(VaultOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var index = Operations.FindIndex(candidate => ReferenceEquals(candidate, operation));

        return index >= 0 ? index : throw new ArgumentException("The operation isn't part of this save.", nameof(operation));
    }

    private void ThrowIfNotSaving(string method)
    {
        if (!_saving)
        {
            throw new InvalidOperationException($"{method} can only be called during {nameof(VaultInterceptor.SavingAsync)}.");
        }
    }

    private static OperationResult ResultOf(VaultOperation operation, ClientBulkWriteResult written, int index) =>
        operation.Kind switch
        {
            OperationKind.Insert => new OperationResult(1, 0, 0, 0),
            OperationKind.Delete => written.DeleteResults.TryGetValue(index, out var deleted)
                ? new OperationResult(0, 0, 0, deleted.DeletedCount)
                : new OperationResult(0, 0, 0, 0),
            _ => written.UpdateResults.TryGetValue(index, out var updated)
                ? new OperationResult(0, updated.MatchedCount, updated.ModifiedCount, 0)
                : new OperationResult(0, 0, 0, 0)
        };
}

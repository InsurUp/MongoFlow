using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

internal static class SavePipeline
{
    public static async Task<SaveResult> RunAsync(VaultRuntime runtime, CancellationToken cancellationToken)
    {
        if (runtime.IsSaving)
        {
            throw new InvalidOperationException($"{runtime.Model.VaultType.Name} can't be saved from its own interceptors.");
        }

        var operations = runtime.Drain();
        if (operations.Count == 0)
        {
            return SaveResult.Empty;
        }

        runtime.IsSaving = true;
        var outer = runtime.Transactions.Active;
        var transaction = outer ?? runtime.Transactions.Start();
        var run = new SaveRun(runtime, operations);
        var callbacks = new SaveCallbacks(run.CommittedAsync, run.FailedAsync);

        try
        {
            await BulkWriteSupport.EnsureAsync(runtime.Model.Database, cancellationToken);

            run.Session = await transaction.JoinAsync(runtime.Model.Client, cancellationToken);
            transaction.Enlist(callbacks);

            await run.SavingAsync(cancellationToken);
            var result = await run.WriteAsync(cancellationToken);
            await run.SavedAsync(cancellationToken);

            if (outer is null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (Exception exception)
        {
            if (!transaction.IsCommitted)
            {
                run.Undo();
            }

            if (outer is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            else
            {
                transaction.Unenlist(callbacks);
                await run.FailedAsync(exception, CancellationToken.None);
            }

            throw;
        }
        finally
        {
            runtime.IsSaving = false;

            if (outer is null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}

/// <summary>One save in progress: the shared operation list and what the steps produce.</summary>
internal sealed class SaveRun(VaultRuntime runtime, List<VaultOperation> operations)
{
    private readonly List<Action> _undo = [];
    private readonly Dictionary<(CollectionModel, string), object?> _queryFilters = [];
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

        foreach (var group in Operations.Where(NeedsOriginal).GroupBy(operation => (CollectionModel)operation.Collection))
        {
            await group.Key.LoadOriginalsAsync(group.ToList(), this, cancellationToken);
        }

        var models = new List<BulkWriteModel>(Operations.Count);
        foreach (var operation in Operations)
        {
            models.Add(await ((CollectionModel)operation.Collection).CreateWriteModelAsync(operation, this, cancellationToken));
        }

        var written = await Runtime.Model.Client.BulkWriteAsync(Session, models,
            new ClientBulkWriteOptions { IsOrdered = true, VerboseResult = true }, cancellationToken);

        for (var i = 0; i < Operations.Count; i++)
        {
            var operation = Operations[i];
            operation.Result = ResultOf(operation, written, i);

            if (operation.Result is { Matched: 0, Deleted: 0 } && ((CollectionModel)operation.Collection).HasConcurrencyToken &&
                operation is { IsSetBased: false, Kind: OperationKind.Replace or OperationKind.Delete, Document: not null })
            {
                throw new ConcurrencyException(operation);
            }
        }

        return Result = new SaveResult(written.InsertedCount, written.MatchedCount, written.ModifiedCount, written.DeletedCount);
    }

    public async Task SavedAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < Runtime.Model.Interceptors.Count; i++)
        {
            await Runtime.GetInterceptor(i).SavedAsync(ContextFor(i), cancellationToken);
        }
    }

    public async Task CommittedAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < Runtime.Model.Interceptors.Count; i++)
        {
            await Runtime.GetInterceptor(i).CommittedAsync(ContextFor(i), cancellationToken);
        }
    }

    /// <summary>Runs every interceptor's failure hook. Their own exceptions are ignored so the original failure surfaces.</summary>
    public async Task FailedAsync(Exception exception, CancellationToken cancellationToken)
    {
        for (var i = 0; i < Runtime.Model.Interceptors.Count; i++)
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

    public void AddUndo(Action undo) => _undo.Add(undo);

    public void Undo()
    {
        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            _undo[i]();
        }

        _undo.Clear();
    }

    /// <summary>A collection's query filters for operations queued with <paramref name="disabled"/> features, resolved once per save.</summary>
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
        interceptor.AppliesTo((CollectionModel)operation.Collection) &&
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

    private bool NeedsOriginal(VaultOperation operation) =>
        operation is { Kind: not OperationKind.Insert, IsSetBased: false } &&
        Runtime.Model.Interceptors.Any(interceptor => interceptor.NeedsOriginals && Sees(interceptor, operation));

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

/// <summary>A save as one interceptor sees it: the shared list, minus operations it doesn't see.</summary>
internal sealed class InterceptorSaveContext(SaveRun run, InterceptorModel interceptor) : SaveContext
{
    public override IMongoVault Vault => run.Runtime.Vault;

    public override IServiceProvider Services => run.Runtime.Services;

    public override IClientSessionHandle Session => run.Session;

    public override IReadOnlyList<VaultOperation> Operations =>
        run.Operations.Where(operation => run.Sees(interceptor, operation)).ToList();

    public override SaveResult? Result => run.Result;

    public override IDictionary<object, object?> Items => run.Items;

    public override void Replace(VaultOperation operation, VaultOperation replacement) => run.Replace(operation, replacement);

    public override void Remove(VaultOperation operation) => run.Remove(operation);
}

internal static class BulkWriteSupport
{
    // 25 is MongoDB 8.0, the first server with client bulk writes.
    private const int ClientBulkWriteWireVersion = 25;

    private static readonly ConditionalWeakTable<IMongoClient, object> Supported = new();

    public static async Task EnsureAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        if (Supported.TryGetValue(database.Client, out _))
        {
            return;
        }

        var hello = await database.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: cancellationToken);
        var wireVersion = hello.GetValue("maxWireVersion", 0).ToInt32();

        if (wireVersion < ClientBulkWriteWireVersion)
        {
            throw new NotSupportedException(
                $"MongoFlow saves with client bulk writes, which need MongoDB 8.0 or later; the server reports wire version {wireVersion}.");
        }

        Supported.TryAdd(database.Client, true);
    }
}

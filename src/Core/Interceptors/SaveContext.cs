using MongoDB.Driver;

namespace MongoFlow;

/// <summary>One save, as an interceptor sees it.</summary>
/// <remarks>
/// A small handle to the save, created for each hook call. Copies refer to the same save. MongoFlow always passes a real
/// one; <c>default(SaveContext)</c> refers to no save and can't be used.
/// </remarks>
public readonly struct SaveContext
{
    private readonly SaveRun _run;
    private readonly int _interceptor;

    internal SaveContext(SaveRun run,
        int interceptor)
    {
        _run = run;
        _interceptor = interceptor;
    }

    /// <summary>The vault being saved.</summary>
    public IMongoVault Vault => _run.Runtime.Vault;

    /// <summary>The request's services.</summary>
    public IServiceProvider Services => _run.Runtime.Services;

    /// <summary>The session of the transaction the save runs in.</summary>
    public IClientSessionHandle Session => _run.Session;

    /// <summary>
    /// The save's operations in queue order, minus those this interceptor doesn't see. Every interceptor works on the same
    /// list, so a change one makes is what the next one sees and what gets written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapshot, so operations can be replaced or removed while going through it; it's taken again once they change.
    /// </para>
    /// <para>
    /// To add a write during <see cref="VaultInterceptor.SavingAsync"/>, queue it on the vault's collections as usual; it's
    /// appended to the list, and interceptors that run later see it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<VaultOperation> Operations => _run.OperationsSeenBy(_interceptor);

    /// <summary>
    /// The documents the vault tracked when the save started, changed or not, in the order they were read, minus those
    /// this interceptor doesn't see. The unchanged ones aren't written; the changed ones, and those a queued write replaces
    /// or deletes, are among <see cref="Operations"/> too. Empty when the vault tracks nothing.
    /// </summary>
    /// <remarks>
    /// For what an interceptor needs from documents whether or not they changed, such as domain events they hold outside
    /// their BSON. A save with nothing to write runs its interceptors only when
    /// <see cref="IVaultBuilder{TVault}.RunSaveWhen{TDocument}"/> says a tracked document needs them.
    /// </remarks>
    public IReadOnlyCollection<object> Tracked => _run.TrackedSeenBy(_interceptor);

    /// <summary>What the save's writes changed. Available from <see cref="VaultInterceptor.SavedAsync"/> on.</summary>
    public SaveResult? Result => _run.Result;

    /// <summary>State shared by every hook of every interceptor during this save.</summary>
    public IDictionary<object, object?> Items => _run.Items;

    /// <summary>Puts <paramref name="replacement"/> where <paramref name="operation"/> was. Only during <see cref="VaultInterceptor.SavingAsync"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="replacement"/> targets a different collection.</exception>
    /// <exception cref="InvalidOperationException">Called after <see cref="VaultInterceptor.SavingAsync"/>.</exception>
    public void Replace(VaultOperation operation, VaultOperation replacement) => _run.Replace(operation, replacement);

    /// <summary>Drops <paramref name="operation"/> from the save. Only during <see cref="VaultInterceptor.SavingAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">Called after <see cref="VaultInterceptor.SavingAsync"/>.</exception>
    public void Remove(VaultOperation operation) => _run.Remove(operation);

    /// <summary>The save itself, for built-in features.</summary>
    internal SaveRun Run => _run;
}

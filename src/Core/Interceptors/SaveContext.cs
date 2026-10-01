using MongoDB.Driver;

namespace MongoFlow;

/// <summary>One save, as an interceptor sees it.</summary>
public abstract class SaveContext
{
    private protected SaveContext()
    {
    }

    public abstract IMongoVault Vault { get; }

    /// <summary>The request's services.</summary>
    public abstract IServiceProvider Services { get; }

    /// <summary>The session of the transaction the save runs in.</summary>
    public abstract IClientSessionHandle Session { get; }

    /// <summary>
    /// The save's operations in queue order, minus those this interceptor doesn't see. Every interceptor works on the same
    /// list, so a change one makes is what the next one sees and what gets written.
    /// </summary>
    /// <remarks>
    /// To add a write during <see cref="VaultInterceptor.SavingAsync"/>, queue it on the vault's collections as usual; it's
    /// appended to the list, and interceptors that run later see it.
    /// </remarks>
    public abstract IReadOnlyList<VaultOperation> Operations { get; }

    /// <summary>What the bulk write changed. Available from <see cref="VaultInterceptor.SavedAsync"/> on.</summary>
    public abstract SaveResult? Result { get; }

    /// <summary>State shared by every hook of every interceptor during this save.</summary>
    public abstract IDictionary<object, object?> Items { get; }

    /// <summary>Puts <paramref name="replacement"/> where <paramref name="operation"/> was. Only during <see cref="VaultInterceptor.SavingAsync"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="replacement"/> targets a different collection.</exception>
    /// <exception cref="InvalidOperationException">Called after <see cref="VaultInterceptor.SavingAsync"/>.</exception>
    public abstract void Replace(VaultOperation operation, VaultOperation replacement);

    /// <summary>Drops <paramref name="operation"/> from the save. Only during <see cref="VaultInterceptor.SavingAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">Called after <see cref="VaultInterceptor.SavingAsync"/>.</exception>
    public abstract void Remove(VaultOperation operation);

    /// <summary>The save itself, for built-in features.</summary>
    internal abstract SaveRun Run { get; }
}

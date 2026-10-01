namespace MongoFlow;

/// <summary>
/// What every vault can do, for app interfaces to extend: <c>interface IPolicyVault : IMongoVault</c>.
/// </summary>
/// <remarks>
/// A vault instance belongs to its scope. Writes can be queued on it from parallel tasks, and its reads can run in
/// parallel while no transaction is open in the scope. A save can't run alongside another save of the same instance.
/// Inside a transaction, including the one a save opens while it runs, reads and saves share one session, and MongoDB
/// runs a transaction's operations one at a time: run them one after another.
/// </remarks>
public interface IMongoVault
{
    /// <summary>
    /// Writes every queued operation, in order, as one client bulk write. It runs in the scope's open transaction if
    /// there is one, and in its own otherwise. Nothing is written if any operation fails: in an open transaction, a save
    /// that fails after writing rolls the transaction back. Queued operations are discarded either way.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from one of this vault's own interceptors, while another save of this vault instance runs, or inside a
    /// transaction on a different client.
    /// </exception>
    Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>The vault's collection of <typeparamref name="TDocument"/>, for code that only knows the document type.</summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one.</exception>
    IVaultCollection<TDocument> Collection<TDocument>();

    /// <summary>The vault's keyed collection of <typeparamref name="TDocument"/>, for code that only knows the types.</summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one, or its key isn't a <typeparamref name="TKey"/>.</exception>
    IVaultCollection<TDocument, TKey> Collection<TDocument, TKey>();
}

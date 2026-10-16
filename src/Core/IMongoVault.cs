namespace MongoFlow;

/// <summary>
/// What every vault can do, for app interfaces to extend: <c>interface IPolicyVault : IMongoVault</c>.
/// </summary>
/// <remarks>
/// <para>
/// A vault instance belongs to its scope. Writes can be queued on it from parallel tasks, and its reads can run in
/// parallel while no transaction is open in the scope. A write queued while a save runs waits for the next save, unless
/// one of the save's interceptors queued it. The saves of a scope's vaults run one after another: one that starts while
/// another is running fails, unless the running save's interceptors started it. Inside a transaction, including the one a
/// save opens while it runs, reads and saves share one session, and MongoDB runs a transaction's operations one at a time:
/// run them one after another.
/// </para>
/// <para>
/// With change tracking (<see cref="IVaultBuilder{TVault}.UseChangeTracking"/>), a save reads the tracked documents to
/// compare them: don't change them while it runs.
/// </para>
/// </remarks>
public interface IMongoVault
{
    /// <summary>
    /// Writes every queued operation, in order, as one client bulk write, after an update for each tracked document that
    /// changed; through a client that encrypts, which the driver refuses client bulk writes, as collection commands, in the
    /// same order. It runs in the scope's open transaction if there is one, and in its own otherwise. Nothing is written if
    /// any operation fails: in an open transaction, a save that fails after writing rolls the transaction back. Queued
    /// operations are discarded either way; tracked changes stay pending until a save that writes them commits, or
    /// <see cref="Reset"/> forgets them. With nothing to write, it returns <see cref="SaveResult.Empty"/> without a
    /// transaction or interceptors, unless <see cref="IVaultBuilder{TVault}.RunSaveWhen{TDocument}"/> says a tracked
    /// document needs them.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from one of this vault's own interceptors, while another save of this vault instance or of another vault of
    /// the scope runs, or inside a transaction on a different client; or a tracked document's key changed.
    /// </exception>
    Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the vault instance over, as a new scope's would be: forgets every document it tracks, so the next read
    /// tracks afresh and the next save compares nothing read before, and discards the writes queued on it. For running a
    /// unit of work again in the same scope, such as after a save that failed with a transient error: the changes a
    /// failed save left pending, made to what the failed attempt read, would otherwise be written with the new ones.
    /// Nothing stored changes, and the scope's open transaction is left as it is, with what its saves wrote.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A save of this vault instance is under way: called from one of the vault's own interceptors, or while it saves on
    /// another task.
    /// </exception>
    void Reset();

    /// <summary>The vault's collection of <typeparamref name="TDocument"/>, for code that only knows the document type.</summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one.</exception>
    IVaultCollection<TDocument> Collection<TDocument>();

    /// <summary>The vault's keyed collection of <typeparamref name="TDocument"/>, for code that only knows the types.</summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one, or its key isn't a <typeparamref name="TKey"/>.</exception>
    IVaultCollection<TDocument, TKey> Collection<TDocument, TKey>();

    /// <summary>
    /// The vault's collection of <paramref name="documentType"/> without its type arguments, keyed or not, for code that
    /// has the type only as a <see cref="Type"/>, such as an interceptor reading <see cref="VaultOperation.Collection"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one.</exception>
    IVaultCollection Collection(Type documentType);

    /// <summary>The vault's keyed collection of <paramref name="documentType"/> without its type arguments.</summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one, or declares it without a key.</exception>
    IKeyedVaultCollection KeyedCollection(Type documentType);
}

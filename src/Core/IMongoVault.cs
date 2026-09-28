namespace MongoFlow;

/// <summary>
/// What every vault can do, for app interfaces to extend: <c>interface IPolicyVault : IMongoVault</c>.
/// </summary>
public interface IMongoVault
{
    /// <summary>
    /// Writes every queued operation, in order, as one client bulk write. It runs in the scope's open transaction if
    /// there is one, and in its own otherwise. Nothing is written if any operation fails. Queued operations are discarded
    /// either way.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from one of this vault's own interceptors, or inside a transaction on a different client.
    /// </exception>
    Task<SaveResult> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>The vault's collection of <typeparamref name="TDocument"/>, for code that only knows the document type.</summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one.</exception>
    IVaultCollection<TDocument> Collection<TDocument>();

    /// <summary>The vault's keyed collection of <typeparamref name="TDocument"/>, for code that only knows the types.</summary>
    /// <exception cref="InvalidOperationException">The vault doesn't declare one, or its key isn't a <typeparamref name="TKey"/>.</exception>
    IVaultCollection<TDocument, TKey> Collection<TDocument, TKey>();
}

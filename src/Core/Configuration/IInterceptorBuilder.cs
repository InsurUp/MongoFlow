namespace MongoFlow;

/// <summary>Declares what an interceptor needs, so each save can be planned before it starts.</summary>
public interface IInterceptorBuilder
{
    /// <summary>
    /// Fills <see cref="VaultOperation.Original"/> for replaces, updates and deletes by key on the collections the
    /// interceptor sees, with one read per collection before the bulk write.
    /// </summary>
    IInterceptorBuilder NeedsOriginals();

    /// <summary>Narrows the collections whose operations the interceptor sees. Evaluated once, at startup.</summary>
    IInterceptorBuilder For(Func<IVaultCollectionInfo, bool> collections);
}

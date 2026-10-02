namespace MongoFlow;

/// <summary>Declares where an interceptor applies, decided once at startup.</summary>
public interface IInterceptorBuilder
{
    /// <summary>Narrows the collections whose operations the interceptor sees. Evaluated once, at startup.</summary>
    IInterceptorBuilder For(Func<IVaultCollectionInfo, bool> collections);
}

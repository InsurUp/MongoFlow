namespace MongoFlow;

internal interface IVaultInterceptorProvider
{
    string? Name { get; }
    
    VaultInterceptor GetInterceptor(IServiceProvider serviceProvider);
}

namespace MongoFlow;

internal interface IVaultInterceptorProvider
{
    VaultInterceptor GetInterceptor(IServiceProvider serviceProvider);
}

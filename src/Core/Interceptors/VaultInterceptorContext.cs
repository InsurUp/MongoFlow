namespace MongoFlow;

public record VaultInterceptorContext(
    MongoVault Vault,
    List<IVaultOperation> Operations,
    IClientSessionHandle Session,
    IServiceProvider ServiceProvider);

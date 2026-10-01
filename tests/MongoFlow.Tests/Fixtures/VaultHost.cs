using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

/// <summary>
/// A <typeparamref name="TVault"/> registered on a service provider of its own, on the <see cref="Offline"/> client's
/// database, and resolved from a scope. Its model is built the first time the vault is resolved.
/// </summary>
public sealed class VaultHost<TVault> : IAsyncDisposable where TVault : MongoVault
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public VaultHost(Action<IVaultBuilder<TVault>>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton(Offline.Client);
        services?.Invoke(collection);
        collection.AddMongoVault<TVault>(vault =>
        {
            vault.UseDatabase(Offline.DatabaseName);
            configure?.Invoke(vault);
        });

        _provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateAsyncScope();
    }

    /// <summary>The scope's vault, resolved on each call; within the scope it's the same instance.</summary>
    public TVault Vault => _scope.ServiceProvider.GetRequiredService<TVault>();

    public IServiceProvider Services => _scope.ServiceProvider;

    public IVaultTransactionManager Transactions => Services.GetRequiredService<IVaultTransactionManager>();

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}

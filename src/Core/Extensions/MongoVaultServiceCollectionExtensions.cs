using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

/// <remarks>
/// Delegates run once, at startup, when the vault is configured. The <see cref="IServiceProvider"/> overloads receive the
/// root provider, for singletons and options; per-request services belong in query filters. <c>AddMongoVault</c> also
/// registers <see cref="IVaultTransactions"/> (scoped) and <see cref="IVaultMigrator"/> (singleton).
/// </remarks>
public static class MongoVaultServiceCollectionExtensions
{
    public static IServiceCollection AddMongoVault<TVault>(this IServiceCollection services,
        Action<IVaultBuilder<TVault>>? configure = null)
        where TVault : MongoVault =>
        throw new NotImplementedException();

    public static IServiceCollection AddMongoVault<TVault>(this IServiceCollection services,
        Action<IServiceProvider, IVaultBuilder<TVault>> configure)
        where TVault : MongoVault =>
        throw new NotImplementedException();

    public static IServiceCollection AddMongoVault<TInterface, TVault>(this IServiceCollection services,
        Action<IVaultBuilder<TVault>>? configure = null)
        where TInterface : class
        where TVault : MongoVault, TInterface =>
        throw new NotImplementedException();

    public static IServiceCollection AddMongoVault<TInterface, TVault>(this IServiceCollection services,
        Action<IServiceProvider, IVaultBuilder<TVault>> configure)
        where TInterface : class
        where TVault : MongoVault, TInterface =>
        throw new NotImplementedException();

    /// <summary>
    /// Applies a configuration to every vault, before the vault's own configuration. Registration order relative to
    /// <c>AddMongoVault</c> does not matter.
    /// </summary>
    /// <param name="configurationType">
    /// An open generic class with one type parameter that implements <see cref="IVaultConfiguration{TVault}"/>, such as
    /// <c>typeof(AuditConfiguration&lt;&gt;)</c>. It is closed over each vault type; vaults that don't satisfy its
    /// constraints are skipped, so constraints can limit a default to some vaults.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="configurationType"/> is not such a class.</exception>
    public static IServiceCollection AddDefaultVaultConfiguration(this IServiceCollection services,
        Type configurationType) =>
        throw new NotImplementedException();
}

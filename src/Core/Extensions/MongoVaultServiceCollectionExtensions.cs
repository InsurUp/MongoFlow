using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MongoFlow;

/// <remarks>
/// Delegates run once, at startup, when the vault is configured. The <see cref="IServiceProvider"/> overloads receive the
/// root provider, for singletons and options; per-request services belong in query filters. <c>AddMongoVault</c> also
/// registers <see cref="IVaultTransactions"/> (scoped) and <see cref="IVaultMigrator"/> (singleton). Calling it again
/// for the same vault adds to its configuration.
/// </remarks>
public static class MongoVaultServiceCollectionExtensions
{
    public static IServiceCollection AddMongoVault<TVault>(this IServiceCollection services,
        Action<IVaultBuilder<TVault>>? configure = null)
        where TVault : MongoVault
    {
        var registration = Register<TVault>(services);
        if (configure is not null)
        {
            registration.Configurations.Add((_, vault) => configure(vault));
        }

        return services;
    }

    public static IServiceCollection AddMongoVault<TVault>(this IServiceCollection services,
        Action<IServiceProvider, IVaultBuilder<TVault>> configure)
        where TVault : MongoVault
    {
        ArgumentNullException.ThrowIfNull(configure);

        Register<TVault>(services).Configurations.Add(configure);
        return services;
    }

    public static IServiceCollection AddMongoVault<TInterface, TVault>(this IServiceCollection services,
        Action<IVaultBuilder<TVault>>? configure = null)
        where TInterface : class
        where TVault : MongoVault, TInterface
    {
        services.AddMongoVault(configure);
        services.TryAddScoped<TInterface>(provider => provider.GetRequiredService<TVault>());

        return services;
    }

    public static IServiceCollection AddMongoVault<TInterface, TVault>(this IServiceCollection services,
        Action<IServiceProvider, IVaultBuilder<TVault>> configure)
        where TInterface : class
        where TVault : MongoVault, TInterface
    {
        services.AddMongoVault(configure);
        services.TryAddScoped<TInterface>(provider => provider.GetRequiredService<TVault>());

        return services;
    }

    /// <summary>
    /// Applies a configuration to every vault, before the vault's own configuration. Registration order relative to
    /// <c>AddMongoVault</c> does not matter.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationType">
    /// An open generic class with one type parameter that implements <see cref="IVaultConfiguration{TVault}"/>, such as
    /// <c>typeof(AuditConfiguration&lt;&gt;)</c>. It is closed over each vault type; vaults that don't satisfy its
    /// constraints are skipped, so constraints can limit a default to some vaults.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="configurationType"/> is not such a class.</exception>
    public static IServiceCollection AddDefaultVaultConfiguration(this IServiceCollection services,
        Type configurationType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationType);

        var isValid = configurationType is { IsGenericTypeDefinition: true, IsAbstract: false, IsInterface: false } &&
                      configurationType.GetGenericArguments() is [var vault] &&
                      configurationType.GetInterfaces().Any(contract =>
                          contract.IsGenericType &&
                          contract.GetGenericTypeDefinition() == typeof(IVaultConfiguration<>) &&
                          contract.GetGenericArguments()[0] == vault);

        if (!isValid)
        {
            throw new ArgumentException(
                $"{configurationType.Name} must be an open generic class with one type parameter, TVault, that implements " +
                "IVaultConfiguration<TVault>, such as typeof(AuditConfiguration<>).",
                nameof(configurationType));
        }

        services.AddSingleton(new DefaultVaultConfiguration(configurationType));
        return services;
    }

    private static VaultRegistration<TVault> Register<TVault>(IServiceCollection services) where TVault : MongoVault
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.FirstOrDefault(service => service.ServiceType == typeof(VaultRegistration<TVault>))?.ImplementationInstance
            is VaultRegistration<TVault> existing)
        {
            return existing;
        }

        var registration = new VaultRegistration<TVault>();

        services.AddSingleton(registration);
        services.AddSingleton<VaultModelProvider<TVault>>();
        services.AddScoped(CreateVault<TVault>);
        services.AddSingleton<VaultMigrationRunner<TVault>>();
        services.AddSingleton<IVaultMigrationRunner>(provider => provider.GetRequiredService<VaultMigrationRunner<TVault>>());

        services.TryAddScoped<VaultTransactions>();
        services.TryAddScoped<IVaultTransactions>(provider => provider.GetRequiredService<VaultTransactions>());
        services.TryAddSingleton<IVaultMigrator, VaultMigrator>();

        return registration;
    }

    private static TVault CreateVault<TVault>(IServiceProvider services) where TVault : MongoVault
    {
        var model = services.GetRequiredService<VaultModelProvider<TVault>>().Model;
        var vault = ActivatorUtilities.CreateInstance<TVault>(services);

        _ = new VaultRuntime(model, services, vault);

        return vault;
    }
}

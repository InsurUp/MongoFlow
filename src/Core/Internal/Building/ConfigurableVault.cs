namespace MongoFlow;

internal static class ConfigurableVault
{
    /// <summary>Calls the vault's static <see cref="IConfigurableVault{TSelf}.Configure"/>, if it has one.</summary>
    public static void Configure<TVault>(IVaultBuilder<TVault> builder) where TVault : MongoVault
    {
        var isConfigurable = typeof(TVault).GetInterfaces().Any(contract =>
            contract.IsGenericType &&
            contract.GetGenericTypeDefinition() == typeof(IConfigurableVault<>) &&
            contract.GetGenericArguments()[0] == typeof(TVault));

        if (!isConfigurable)
        {
            return;
        }

        var configure = typeof(Invoker<>)
            .MakeGenericType(typeof(TVault))
            .GetMethod(nameof(Invoker<>.Configure))!
            .CreateDelegate<Action<IVaultBuilder<TVault>>>();

        configure(builder);
    }

    private static class Invoker<TVault> where TVault : MongoVault, IConfigurableVault<TVault>
    {
        public static void Configure(IVaultBuilder<TVault> builder) => TVault.Configure(builder);
    }
}

namespace MongoFlow;

/// <summary>
/// Setup for <typeparamref name="TVault"/>. Apply it to one vault with
/// <see cref="IVaultBuilder{TVault}.UseConfiguration{TConfiguration}"/>, or declare the class generic over
/// <typeparamref name="TVault"/> and register it with <c>AddDefaultVaultConfiguration</c> to apply it to every vault.
/// </summary>
/// <remarks>
/// Created from the root provider once, when the vault is configured, so it can depend on singletons and options, but
/// not on scoped services, which the root provider would resolve for the app's life.
/// </remarks>
public interface IVaultConfiguration<TVault> where TVault : MongoVault
{
    /// <summary>Configures the vault, once.</summary>
    void Configure(IVaultBuilder<TVault> vault);
}

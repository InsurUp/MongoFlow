namespace MongoFlow;

/// <summary>
/// Lets a vault class configure itself. <see cref="Configure"/> runs once, at startup, after default configurations and
/// before the registration delegate.
/// </summary>
/// <remarks>
/// It is static so it cannot read instance state, which would otherwise leak into the model every request shares.
/// </remarks>
public interface IConfigurableVault<TSelf> where TSelf : MongoVault, IConfigurableVault<TSelf>
{
    /// <summary>Configures the vault, once.</summary>
    static abstract void Configure(IVaultBuilder<TSelf> vault);
}

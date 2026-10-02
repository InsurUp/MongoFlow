namespace MongoFlow;

/// <summary>A vault's configuration is invalid. Thrown while the vault's model is built, before first use.</summary>
public sealed class VaultConfigurationException(string message) : Exception(message);

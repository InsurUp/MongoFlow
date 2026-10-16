namespace MongoFlow.Encryption;

/// <summary>Queryable Encryption, as a vault feature: what <c>UseQueryableEncryption</c> adds.</summary>
internal sealed class EncryptionFeature(QueryableEncryptionOptions options) : IVaultFeature
{
    public static FeatureKey Key => QueryableEncryptionFeature.Key;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault
    {
        var builder = (VaultModelBuilderBase)vault;
        builder.UseEncryption(new QueryableEncryption(options, builder.Services));
    }
}

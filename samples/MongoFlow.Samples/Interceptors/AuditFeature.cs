using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Interceptors;

/// <summary>
/// Records every change in the audit vault. It's a feature, rather than a bare interceptor, so a bulk import can switch
/// it off with <c>Without(AuditFeature.Key)</c>.
/// </summary>
public sealed class AuditFeature : IVaultFeature
{
    public static FeatureKey Key { get; } = new("audit");

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
        vault.AddInterceptor<AuditTrailInterceptor>(interceptor => interceptor
            .For(collection => collection.DocumentType != typeof(AuditLogEntry)));
}

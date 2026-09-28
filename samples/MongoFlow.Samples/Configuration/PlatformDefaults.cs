using Microsoft.Extensions.DependencyInjection;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Interceptors;

namespace MongoFlow.Samples.Configuration;

/// <summary>
/// The platform's data rules, applied to every vault. Each rule only touches collections whose documents it concerns,
/// so vaults without tenant-owned or soft-deletable documents are unaffected. Interceptors run in the order listed.
/// </summary>
public sealed class PlatformDefaults<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
{
    public void Configure(IVaultBuilder<TVault> vault) => vault
        .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
        .UseMultiTenancy(
            (ITenantOwned x) => x.AgencyId,
            services => services.GetRequiredService<ICurrentUser>().AgencyId,
            allTenants: services => services.GetRequiredService<ICurrentUser>().IsPlatformAdmin)
        .AddFeature<PermissionFeature>()
        .AddFeature<ModuleFeature>()
        .AddFeature<TimestampFeature>()
        .AddFeature<AuditFeature>()
        .AddInterceptor<OutboxInterceptor>(); // not in a feature, so it can't be switched off
}

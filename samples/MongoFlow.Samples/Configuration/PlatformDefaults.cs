using Microsoft.Extensions.DependencyInjection;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Configuration;

/// <summary>
/// The platform's data rules, applied to every vault. Each rule only touches collections whose documents it concerns,
/// so vaults without tenant-owned or soft-deletable documents are unaffected.
/// </summary>
public sealed class PlatformDefaults<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
{
    public void Configure(IVaultBuilder<TVault> vault) => vault
        .UseSoftDelete((ISoftDeletable x) => x.IsDeleted)
        .UseMultiTenancy(
            (ITenantOwned x) => x.AgencyId,
            services => services.GetRequiredService<ICurrentUser>().AgencyId)
        .AddFeature<PermissionFeature>()
        .AddFeature<ModuleFeature>();
}

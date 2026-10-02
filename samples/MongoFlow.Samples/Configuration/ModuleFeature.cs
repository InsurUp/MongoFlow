using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Configuration;

/// <summary>Hides documents of a <see cref="ModuleAttribute"/> module from agencies that haven't bought it.</summary>
public sealed class ModuleFeature : IVaultFeature, IVaultCollectionConfiguration
{
    public static FeatureKey Key { get; } = new("modules");

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault.ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        var module = typeof(TDocument).GetCustomAttribute<ModuleAttribute>()?.Name;
        if (module is null)
        {
            return;
        }

        collection.QueryFilter(async (services, cancellationToken) =>
        {
            var user = services.GetRequiredService<ICurrentUser>();

            // Unauthenticated users are already shut out by the permission feature; platform admins see every module.
            if (!user.IsAuthenticated || user.IsPlatformAdmin)
            {
                return _ => true;
            }

            var modules = await user.GetModulesAsync(cancellationToken);

            return modules.Contains(module) ? _ => true : _ => false;
        });
    }
}

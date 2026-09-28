using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Configuration;

/// <summary>
/// Documents marked <see cref="RequiresPermissionAttribute"/> are visible only to users holding that permission; users
/// holding only its <c>.own</c> variant see the documents they own. Permissions are loaded asynchronously, per query.
/// </summary>
public sealed class PermissionFeature : IVaultFeature, IVaultCollectionConfiguration
{
    // Callers need the key without an instance, to switch the feature off, but IVaultFeature asks for an instance
    // property, so the static one needs a different name.
    public static readonly FeatureKey FeatureKey = new("permissions");

    public FeatureKey Key => FeatureKey;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault.ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        var permission = typeof(TDocument).GetCustomAttribute<RequiresPermissionAttribute>()?.Permission;
        if (permission is null)
        {
            return;
        }

        var ownable = typeof(TDocument).IsAssignableTo(typeof(IOwnedByUser));

        collection.QueryFilter(async (services, cancellationToken) =>
        {
            var user = services.GetRequiredService<ICurrentUser>();

            if (!user.IsAuthenticated)
            {
                return _ => false;
            }

            if (user.IsPlatformAdmin)
            {
                return _ => true;
            }

            var permissions = await user.GetPermissionsAsync(cancellationToken);

            if (permissions.Contains(permission))
            {
                return _ => true;
            }

            if (ownable && permissions.Contains(permission + ".own"))
            {
                var userId = user.UserId;
                return document => ((IOwnedByUser)document!).OwnerUserId == userId;
            }

            return _ => false;
        });
    }
}

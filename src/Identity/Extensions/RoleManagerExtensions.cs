using Microsoft.AspNetCore.Identity;

namespace MongoFlow.Identity;

/// <summary>What the <see cref="RoleManager{TRole}"/> <c>AddMongoFlowStores</c> registers adds to Identity's.</summary>
public static class RoleManagerExtensions
{
    /// <summary>
    /// A copy of the manager whose reads and writes are made with a vault feature switched off, such as
    /// <c>roleManager.Without(MultiTenancyFeature.Key)</c> to reach every tenant's roles.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manager isn't the one <c>AddMongoFlowStores</c> registers.</exception>
    public static RoleManager<TRole> Without<TRole>(this RoleManager<TRole> roleManager,
        FeatureKey feature)
        where TRole : class
    {
        ArgumentNullException.ThrowIfNull(roleManager);

        return roleManager is RoleManagerWrapper<TRole> wrapper
            ? wrapper.Without(feature)
            : throw new InvalidOperationException($"Without needs the RoleManager that AddMongoFlowStores registers.");
    }
}

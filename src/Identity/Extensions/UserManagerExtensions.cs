using Microsoft.AspNetCore.Identity;

namespace MongoFlow.Identity;

public static class UserManagerExtensions
{
    /// <summary>
    /// A copy of the manager whose reads and writes are made with a vault feature switched off, such as
    /// <c>userManager.Without(MultiTenancyFeature.Key)</c> to reach every tenant's users.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manager isn't the one <c>AddMongoFlowStores</c> registers.</exception>
    public static UserManager<TUser> Without<TUser>(this UserManager<TUser> userManager,
        FeatureKey feature)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(userManager);

        return userManager is UserManagerWrapper<TUser> wrapper
            ? wrapper.Without(feature)
            : throw new InvalidOperationException($"Without needs the UserManager that AddMongoFlowStores registers.");
    }
}

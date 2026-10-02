using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MongoFlow.Identity;

public static class MongoFlowIdentityBuilderExtensions
{
    /// <summary>
    /// Stores Identity's users and roles in <typeparamref name="TVault"/>, registered with <c>AddMongoVault</c>. Call it
    /// after <c>AddRoles</c>.
    /// </summary>
    /// <remarks>
    /// The <see cref="UserManager{TUser}"/> and <see cref="RoleManager{TRole}"/> it registers can switch a vault feature
    /// off with <see cref="UserManagerExtensions.Without{TUser}"/> and <see cref="RoleManagerExtensions.Without{TRole}"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TVault"/> doesn't derive from <see cref="IdentityMongoVault{TUser, TRole, TKey}"/>, Identity's
    /// user or role types don't derive from <see cref="MongoUser{TKey}"/> and <see cref="MongoRole{TKey}"/> or aren't the
    /// vault's, or <c>AddRoles</c> wasn't called.
    /// </exception>
    public static IdentityBuilder AddMongoFlowStores<TVault>(this IdentityBuilder builder) where TVault : MongoVault
    {
        ArgumentNullException.ThrowIfNull(builder);

        var vault = FindGenericBase(typeof(TVault), typeof(IdentityMongoVault<,,>))
            ?? throw new InvalidOperationException(
                $"AddMongoFlowStores needs a vault derived from IdentityMongoVault<TUser, TRole, TKey>, but {typeof(TVault).Name} isn't.");

        var userType = builder.UserType;
        if (FindGenericBase(userType, typeof(MongoUser<>)) is null)
        {
            throw new InvalidOperationException(
                $"AddMongoFlowStores needs users derived from MongoUser<TKey>, but {userType.Name} isn't.");
        }

        var roleType = builder.RoleType ?? throw new InvalidOperationException("Call AddRoles<TRole> before AddMongoFlowStores.");
        if (FindGenericBase(roleType, typeof(MongoRole<>)) is null)
        {
            throw new InvalidOperationException(
                $"AddMongoFlowStores needs roles derived from MongoRole<TKey>, but {roleType.Name} isn't.");
        }

        var (vaultUser, vaultRole, keyType) = (vault.GenericTypeArguments[0], vault.GenericTypeArguments[1], vault.GenericTypeArguments[2]);
        if (vaultUser != userType || vaultRole != roleType)
        {
            throw new InvalidOperationException(
                $"{typeof(TVault).Name} stores users of {vaultUser.Name} and roles of {vaultRole.Name}, but Identity is " +
                $"configured with {userType.Name} and {roleType.Name}.");
        }

        var services = builder.Services;
        var userManager = typeof(UserManager<>).MakeGenericType(userType);
        var roleManager = typeof(RoleManager<>).MakeGenericType(roleType);
        var userManagerWrapper = typeof(UserManagerWrapper<>).MakeGenericType(userType);
        var roleManagerWrapper = typeof(RoleManagerWrapper<>).MakeGenericType(roleType);

        services.AddScoped(typeof(IUserStore<>).MakeGenericType(userType),
            typeof(MongoUserStore<,,,>).MakeGenericType(typeof(TVault), userType, roleType, keyType));
        services.AddScoped(typeof(IRoleStore<>).MakeGenericType(roleType),
            typeof(MongoRoleStore<,,>).MakeGenericType(typeof(TVault), roleType, keyType));

        // The managers that can switch a feature off take the place of Identity's own.
        services.RemoveAll(userManager);
        services.RemoveAll(roleManager);
        services.AddScoped(userManager, provider => ActivatorUtilities.CreateInstance(provider, userManagerWrapper));
        services.AddScoped(roleManager, provider => ActivatorUtilities.CreateInstance(provider, roleManagerWrapper));

        MongoIdentityConfiguration.ConfigureByType(keyType);

        return builder;
    }

    private static Type? FindGenericBase(Type type,
        Type genericDefinition)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == genericDefinition)
            {
                return current;
            }
        }

        return null;
    }
}

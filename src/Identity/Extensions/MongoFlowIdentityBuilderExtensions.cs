using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoFlow.Identity.Wrappers;

namespace MongoFlow.Identity;

public static class MongoFlowIdentityBuilderExtensions
{
    public static IdentityBuilder AddMongoFlowStores<TVault>(this IdentityBuilder builder)
        where TVault : MongoVault
    {
        var vaultType = FindGenericBaseType(typeof(TVault), typeof(IdentityMongoVault<,,>));
        if (vaultType is null)
        {
            throw new InvalidOperationException("AddMongoFlowStores can only be called with a vault that derives from IdentityMongoVault<TUser, TRole, TKey>");
        }
        
        var userType = builder.UserType;
        var roleType = builder.RoleType;
        
        var mongoUserType = FindGenericBaseType(userType, typeof(MongoUser<>));
        if (mongoUserType == null)
        {
            throw new InvalidOperationException("AddMongoFlowStores can only be called with a user that derives from MongoUser<Key>");
        }

        if (roleType is null)
        {
            throw new InvalidOperationException("AddRoles<TRole> must be called before AddMongoFlowStores");
        }
        
        var mongoRoleType = FindGenericBaseType(roleType, typeof(MongoRole<>));
        if (mongoRoleType == null)
        {
            throw new InvalidOperationException("AddMongoFlowStores can only be called with a role that derives from MongoRole<Key>");
        }
        
        var keyType = mongoUserType.GenericTypeArguments[0];
        
        var userStoreType = typeof(MongoUserStore<,,,>).MakeGenericType(typeof(TVault), userType, roleType, keyType);
        var roleStoreType = typeof(MongoRoleStore<,,>).MakeGenericType(typeof(TVault), roleType, keyType);
        var userManagerType = typeof(UserManager<>).MakeGenericType(userType);
        var roleManagerType = typeof(RoleManager<>).MakeGenericType(roleType);
        var userManagerWrapperType = typeof(UserManagerWrapper<>).MakeGenericType(userType);
        var roleManagerWrapperType = typeof(RoleManagerWrapper<>).MakeGenericType(roleType);
        
        builder.Services.AddScoped(typeof(IUserStore<>).MakeGenericType(userType), userStoreType);
        builder.Services.AddScoped(typeof(IRoleStore<>).MakeGenericType(roleType), roleStoreType);

        builder.Services.RemoveAll(userManagerType);
        builder.Services.RemoveAll(roleManagerType);
        
        builder.Services.AddScoped(userManagerType, serviceProvider => ActivatorUtilities.CreateInstance(serviceProvider, userManagerWrapperType));
        builder.Services.AddScoped(roleManagerType, serviceProvider => ActivatorUtilities.CreateInstance(serviceProvider, roleManagerWrapperType));
        
        MongoIdentityConfiguration.ConfigureByType(keyType);
        
        return builder;
    }
    
    private static Type? FindGenericBaseType(Type currentType, Type genericBaseType)
    {
        var type = currentType;
        while (type != null)
        {
            var genericType = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
            if (genericType != null && genericType == genericBaseType)
            {
                return type;
            }
            type = type.BaseType;
        }
        return null;
    }
    
}
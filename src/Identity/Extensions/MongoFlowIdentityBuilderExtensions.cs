using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Identity;

public static class MongoFlowIdentityBuilderExtensions
{
    public static IdentityBuilder AddMongoFlowStores<TVault>(this IdentityBuilder builder)
        where TVault : IdentityMongoVault
    {
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
        
        builder.Services.AddScoped(typeof(IUserStore<>).MakeGenericType(userType), userStoreType);
        builder.Services.AddScoped(typeof(IRoleStore<>).MakeGenericType(roleType), roleStoreType);
        
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
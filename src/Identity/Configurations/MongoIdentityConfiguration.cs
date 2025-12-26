using System.Reflection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson.Serialization;

namespace MongoFlow.Identity;

internal static class MongoIdentityConfiguration
{
    private static readonly MethodInfo ConfigureMethod = typeof(MongoIdentityConfiguration)
        .GetMethod(nameof(Configure), 1, [])!;
    
    public static void ConfigureByType(Type type)
    {
        ConfigureMethod.MakeGenericMethod(type).Invoke(null, null);
    }
    
    public static void Configure<TKey>() where TKey : IEquatable<TKey>
    {
        BsonClassMap.TryRegisterClassMap<IdentityUserClaim<TKey>>(map =>
        {
            map.AutoMap();
            map.SetIgnoreExtraElements(true);
            map.UnmapProperty(x => x.Id);
            map.UnmapProperty(x => x.UserId);
        });
        
        BsonClassMap.TryRegisterClassMap<IdentityUserLogin<TKey>>(map =>
        {
            map.AutoMap();
            map.SetIgnoreExtraElements(true);
            map.UnmapProperty(x => x.UserId);
        });
        
        BsonClassMap.TryRegisterClassMap<MongoUserToken<TKey>>(map =>
        {
            map.AutoMap();
            map.SetIgnoreExtraElements(true);
            map.UnmapProperty(x => x.Id);
        });
        
        BsonClassMap.TryRegisterClassMap<IdentityRoleClaim<TKey>>(map =>
        {
            map.AutoMap();
            map.SetIgnoreExtraElements(true);
            map.UnmapProperty(x => x.Id);
            map.UnmapProperty(x => x.RoleId);
        });
        
        BsonClassMap.TryRegisterClassMap<IdentityUserPasskey<TKey>>(map =>
        {
            map.AutoMap();
            map.SetIgnoreExtraElements(true);
            map.UnmapProperty(x => x.UserId);
        });
    }
    
}
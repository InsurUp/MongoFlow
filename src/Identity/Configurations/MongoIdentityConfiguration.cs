using System.Reflection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson.Serialization;

namespace MongoFlow.Identity;

/// <summary>
/// How Identity's own types are stored. Claims, logins and passkeys live inside their user or role, so the members
/// pointing back at it (<c>UserId</c>, <c>RoleId</c>) and the claims' unused <c>Id</c> aren't stored; unknown elements
/// are ignored, so documents written by other versions still read.
/// </summary>
/// <remarks>Class maps are global to the driver, so each is registered once, for every key type in use.</remarks>
internal static class MongoIdentityConfiguration
{
    private static readonly MethodInfo ConfigureMethod = typeof(MongoIdentityConfiguration).GetMethod(nameof(Configure), 1, [])!;

    public static void ConfigureByType(Type keyType) => ConfigureMethod.MakeGenericMethod(keyType).Invoke(null, null);

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

using System.Collections.Concurrent;

namespace MongoFlow;

internal static class VaultPropertyCache
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, VaultProperty>> Cache = [];

    public static IReadOnlyDictionary<Type, VaultProperty> GetProperties<TVault>() where TVault : MongoVault
    {
        return GetProperties(typeof(TVault));
    }

    public static IReadOnlyDictionary<Type, VaultProperty> GetProperties(Type vaultType)
    {
        if (Cache.TryGetValue(vaultType, out var properties))
        {
            return properties;
        }

        properties = new ConcurrentDictionary<Type, VaultProperty>();

        foreach (var property in vaultType.GetProperties())
        {
            if (property.PropertyType.IsGenericType &&
                property.PropertyType.GetGenericTypeDefinition() == typeof(DocumentSet<>))
            {
                var documentType = property.PropertyType.GetGenericArguments()[0];
                properties[documentType] = new VaultProperty(property.Name, documentType, property);
            }
        }

        Cache[vaultType] = properties;

        return properties;
    }
}

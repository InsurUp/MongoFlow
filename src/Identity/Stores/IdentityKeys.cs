using System.ComponentModel;
using MongoDB.Bson;

namespace MongoFlow.Identity;

internal static class IdentityKeys
{
    /// <summary>
    /// The key in <paramref name="id"/>, as Identity passes it around, or the default when it's empty. An
    /// <see cref="ObjectId"/>, the default key, has no type converter, so it's parsed here; one that doesn't parse finds
    /// nothing, like a key no document has.
    /// </summary>
    public static TKey? FromString<TKey>(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return default;
        }

        if (typeof(TKey) == typeof(ObjectId))
        {
            return (TKey)(object)(ObjectId.TryParse(id, out var objectId) ? objectId : ObjectId.Empty);
        }

        return (TKey?)TypeDescriptor.GetConverter(typeof(TKey)).ConvertFromInvariantString(id);
    }
}

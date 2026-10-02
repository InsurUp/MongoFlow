using MongoDB.Bson;

namespace MongoFlow.IntegrationTests;

/// <summary>Identity's documents as stored, without what changes from run to run, for snapshots.</summary>
public static class IdentityDocuments
{
    /// <summary>
    /// <paramref name="documents"/> without the security and concurrency stamps, which Identity sets at random, and
    /// without generated ids when <paramref name="withoutIds"/>.
    /// </summary>
    public static List<BsonDocument> Stable(List<BsonDocument> documents,
        bool withoutIds = false)
    {
        foreach (var document in documents)
        {
            document.Remove("SecurityStamp");
            document.Remove("ConcurrencyStamp");

            if (withoutIds)
            {
                document.Remove("_id");
            }
        }

        return documents;
    }
}

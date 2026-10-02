using MongoDB.Driver;

namespace MongoFlow.Identity;

internal static class IdentityWrites
{
    private const int WriteConflict = 112;

    /// <summary>
    /// Whether a save failed because another transaction is changing one of its documents: MongoDB fails the second
    /// write at once rather than waiting.
    /// </summary>
    public static bool IsWriteConflict(ClientBulkWriteException exception) =>
        exception.InnerException is MongoCommandException { Code: WriteConflict };
}

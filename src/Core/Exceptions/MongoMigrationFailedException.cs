namespace MongoFlow;

public sealed class MongoMigrationFailedException : Exception
{
    internal MongoMigrationFailedException(MigrateResult result) : base("Mongo vault migration has been failed!", result.Exception)
    {
    }
}
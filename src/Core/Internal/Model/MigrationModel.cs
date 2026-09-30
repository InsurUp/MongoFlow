namespace MongoFlow;

internal sealed record MigrationModel(IReadOnlyList<Type> Types, string CollectionName);

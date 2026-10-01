namespace MongoFlow;

/// <summary>A vault's migrations, as declared: their types, and the collection that records the applied ones.</summary>
internal sealed record MigrationModel(IReadOnlyList<Type> Types, string CollectionName);

using Semver;

namespace MongoFlow;

/// <summary>
/// A vault's migrations, as declared: their types, the collection that records the applied ones, and the version its
/// <see cref="MongoVersionAttribute"/> migrates it to, if it has one.
/// </summary>
internal sealed record MigrationModel(IReadOnlyList<Type> Types,
    string CollectionName,
    SemVersion? Target);

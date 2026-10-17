using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// What setting up a collection's declared settings does: nothing, create it, change it in place with
/// <see cref="Changes"/>, or nothing but fail, for what MongoDB can't change, in <see cref="Conflicts"/>.
/// </summary>
internal sealed record CollectionPlan(ICollectionModel Collection,
    SetupAction Action,
    BsonDocument? Changes = null,
    IReadOnlyList<string>? Conflicts = null);

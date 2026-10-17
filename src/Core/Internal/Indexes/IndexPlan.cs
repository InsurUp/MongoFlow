using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// What setting up a declared index does: nothing, create it, change it in place with <see cref="Changes"/>, or nothing
/// but fail, for what MongoDB can't change, in <see cref="Conflicts"/>.
/// </summary>
internal sealed record IndexPlan(IndexDeclaration Declaration,
    SetupAction Action,
    BsonDocument? Changes = null,
    IReadOnlyList<string>? Conflicts = null);

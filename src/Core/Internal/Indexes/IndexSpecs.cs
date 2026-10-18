using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// Compares a declared index with the server's: what differs that MongoDB can't change in place, and what it can, its
/// expiry and whether it's hidden.
/// </summary>
internal static class IndexSpecs
{
    /// <summary>
    /// Why the server would refuse <paramref name="index"/>, or <see langword="null"/>: it has no keys, is on <c>_id</c>
    /// alone, which every collection has an index on, or expires documents by more than one field.
    /// </summary>
    public static string? Invalid(IndexDeclaration index) =>
        index.Keys.ElementCount switch
        {
            0 => "it has no keys",
            1 when index.Keys.GetElement(0).Name == "_id" => "it's on _id alone, which every collection has an index on",
            > 1 when index.ExpireAfter is not null => "it expires documents by more than one field, where MongoDB takes one",
            _ => null
        };

    /// <summary>How <paramref name="index"/> is named in a message: by its name, or as an index when it has none.</summary>
    public static string Described(IndexDeclaration index) => index.Name.Length > 0 ? $"the index {index.Name}" : "an index";

    /// <summary>The paths <paramref name="index"/>'s keys index, without the <c>$**</c> of a wildcard, or <c>$**</c> alone.</summary>
    public static IEnumerable<string> Paths(IndexDeclaration index) =>
        index.Keys.Names.Select(name => name.EndsWith(".$**", StringComparison.Ordinal) ? name[..^4] : name);

    /// <summary>The index as <c>listIndexes</c> lists it, for comparing and for the index check.</summary>
    public static BsonDocument Listed(IndexDeclaration index)
    {
        var (keys, weights) = ListedKeys(index.Keys);
        var listed = new BsonDocument { { "key", keys }, { "name", index.Name } };

        listed.AddIfNotNull("weights", weights);
        if (index.Unique)
        {
            listed.Add("unique", true);
        }

        if (index.Sparse)
        {
            listed.Add("sparse", true);
        }

        listed.AddIfNotNull("partialFilterExpression", index.Partial);
        if (index.ExpireAfter is { } expiry)
        {
            listed.Add("expireAfterSeconds", (long)expiry.TotalSeconds);
        }

        if (index.Hidden)
        {
            listed.Add("hidden", true);
        }

        listed.AddIfNotNull("collation", StoredSpecs.Collation(index.Collation));
        return listed;
    }

    /// <summary>
    /// What differs between <paramref name="declared"/> and <paramref name="listed"/>, the server's index of its name, in
    /// a collection that compares strings with <paramref name="collation"/>, unless the index says otherwise.
    /// </summary>
    public static SpecComparison Compare(IndexDeclaration declared,
        BsonDocument listed,
        BsonDocument? collation)
    {
        var conflicts = new List<string>();
        var (keys, weights) = ListedKeys(declared.Keys);

        if (!StoredSpecs.SameKeys(keys, listed["key"].AsBsonDocument))
        {
            conflicts.Add($"its keys are {listed["key"].ToJson()}, not {declared.Keys.ToJson()}");
        }
        else if (weights is not null && !StoredSpecs.Same(weights, listed.GetValue("weights", BsonNull.Value)))
        {
            conflicts.Add($"it weighs its text as {listed.GetValue("weights", BsonNull.Value).ToJson()}, not {weights.ToJson()}");
        }

        if (Flag(listed, "unique") != declared.Unique)
        {
            conflicts.Add(declared.Unique ? "it isn't unique, and is declared unique" : "it's unique, and isn't declared unique");
        }

        if (Flag(listed, "sparse") != declared.Sparse)
        {
            conflicts.Add(declared.Sparse ? "it isn't sparse, and is declared sparse" : "it's sparse, and isn't declared sparse");
        }

        var partial = listed.GetValue("partialFilterExpression", BsonNull.Value);
        if (!StoredSpecs.Same(partial, declared.Partial))
        {
            conflicts.Add((partial, declared.Partial) switch
            {
                (BsonNull, { } wanted) => $"it has no partial filter, and is declared with {wanted.ToJson()}",
                (_, null) => $"it has the partial filter {partial.ToJson()}, and is declared without one",
                _ => $"its partial filter is {partial.ToJson()}, not {declared.Partial!.ToJson()}"
            });
        }

        var expected = Collation(declared, collation);
        var stored = listed.GetValue("collation", BsonNull.Value) as BsonDocument;
        if (!StoredSpecs.SameCollation(expected, stored))
        {
            conflicts.Add($"it compares strings with {StoredSpecs.Describe(stored)}, not {StoredSpecs.Describe(expected)}");
        }

        var changes = new BsonDocument("name", declared.Name);
        var expiry = listed.GetValue("expireAfterSeconds", BsonNull.Value);
        if (declared.ExpireAfter is { } expireAfter)
        {
            if (!StoredSpecs.Same(expiry, (long)expireAfter.TotalSeconds))
            {
                changes.Add("expireAfterSeconds", (long)expireAfter.TotalSeconds);
            }
        }
        else if (!expiry.IsBsonNull)
        {
            conflicts.Add($"it expires documents after {expiry} s, and is declared not to, which MongoDB can't change in place");
        }

        if (Flag(listed, "hidden") != declared.Hidden)
        {
            changes.Add("hidden", declared.Hidden);
        }

        return new SpecComparison(conflicts, changes.ElementCount > 1 ? changes : null);
    }

    /// <summary>
    /// Whether the server keeps one index of <paramref name="declared"/> and <paramref name="listed"/>, which it refuses
    /// two of under different names: the same keys, collation and partial filter, both unique or not, and both sparse or
    /// not.
    /// </summary>
    public static bool Equivalent(IndexDeclaration declared,
        BsonDocument listed,
        BsonDocument? collation) =>
        StoredSpecs.SameKeys(ListedKeys(declared.Keys).Keys, listed["key"].AsBsonDocument) &&
        StoredSpecs.SameCollation(Collation(declared, collation), listed.GetValue("collation", BsonNull.Value) as BsonDocument) &&
        StoredSpecs.Same(listed.GetValue("partialFilterExpression", BsonNull.Value), declared.Partial) &&
        Flag(listed, "unique") == declared.Unique &&
        Flag(listed, "sparse") == declared.Sparse;

    /// <summary>
    /// What to do about <paramref name="declared"/>, given the indexes the server lists of a collection that compares
    /// strings with <paramref name="collation"/>.
    /// </summary>
    public static IndexPlan Plan(IndexDeclaration declared,
        IReadOnlyList<BsonDocument> listed,
        BsonDocument? collation)
    {
        if (listed.FirstOrDefault(index => NameOf(index) == declared.Name) is { } named)
        {
            var comparison = Compare(declared, named, collation);
            return comparison switch
            {
                { Conflicts.Count: > 0 } => new IndexPlan(declared, SetupAction.Conflict, Conflicts: comparison.Conflicts),
                { Changes: { } changes } => new IndexPlan(declared, SetupAction.Update, changes),
                _ => new IndexPlan(declared, SetupAction.Unchanged)
            };
        }

        if (listed.FirstOrDefault(index => NameOf(index) != "_id_" && Equivalent(declared, index, collation)) is { } equivalent)
        {
            var conflict = $"the index {NameOf(equivalent)} is on the same keys, with the same options, which MongoDB keeps one index of";
            return new IndexPlan(declared, SetupAction.Conflict, Conflicts: [conflict]);
        }

        return new IndexPlan(declared, SetupAction.Create);
    }

    /// <summary>
    /// What syncing <paramref name="declared"/> does, given the indexes the server lists of a collection that compares
    /// strings with <paramref name="collation"/>, and the names of every index declared of it. As <see cref="Plan"/>, but
    /// a plain index of its name is made unique in place, and one that differs in anything else MongoDB can't change in
    /// place is recreated, as is one on the same keys with the same options under a name no vault declares.
    /// </summary>
    public static IndexPlan PlanSync(IndexDeclaration declared,
        IReadOnlyList<BsonDocument> listed,
        BsonDocument? collation,
        IReadOnlySet<string> declaredNames)
    {
        if (listed.FirstOrDefault(index => NameOf(index) == declared.Name) is { } named)
        {
            var comparison = Compare(declared, named, collation);
            return comparison switch
            {
                { Conflicts.Count: 0, Changes: { } changes } => new IndexPlan(declared, SetupAction.Update, changes),
                { Conflicts.Count: 0 } => new IndexPlan(declared, SetupAction.Unchanged),

                // Declared unique, and otherwise the same: collMod makes it unique, unless documents share its keys.
                { Conflicts.Count: 1 } when declared.Unique && !Flag(named, "unique") =>
                    new IndexPlan(declared, SetupAction.MakeUnique, comparison.Changes),

                _ => new IndexPlan(declared, SetupAction.Recreate, Conflicts: comparison.Conflicts, Replaces: named)
            };
        }

        if (listed.FirstOrDefault(index => NameOf(index) != "_id_" && Equivalent(declared, index, collation)) is { } equivalent)
        {
            // The server keeps one index of the two: one declared is a conflict, as it is without syncing.
            return declaredNames.Contains(NameOf(equivalent)!)
                ? new IndexPlan(declared, SetupAction.Conflict,
                    Conflicts: [$"the index {NameOf(equivalent)}, declared too, is on the same keys, with the same options, which MongoDB keeps one index of"])
                : new IndexPlan(declared, SetupAction.Recreate, Replaces: equivalent);
        }

        return new IndexPlan(declared, SetupAction.Create);
    }

    /// <summary>
    /// The indexes the server lists that syncing drops: those no vault declares, but <c>_id_</c> and Queryable Encryption's
    /// on <c>__safeContent__</c>, which the server keeps itself, and those <paramref name="plans"/> replace already.
    /// </summary>
    public static IReadOnlyList<BsonDocument> Undeclared(IReadOnlyList<BsonDocument> listed,
        IReadOnlyList<IndexPlan> plans)
    {
        var kept = plans.Select(plan => plan.Declaration.Name)
            .Concat(plans.Where(plan => plan.Replaces is not null).Select(plan => NameOf(plan.Replaces!)!))
            .ToHashSet(StringComparer.Ordinal);

        return [.. listed.Where(index => NameOf(index) is { } name && !kept.Contains(name) && !IsServers(index))];
    }

    /// <summary>
    /// Whether an index on <paramref name="keys"/>, as the server lists them, backs a collection sharded on
    /// <paramref name="shardKey"/>: its keys start with the shard key's.
    /// </summary>
    public static bool Backs(BsonDocument keys,
        BsonDocument shardKey) =>
        keys.ElementCount >= shardKey.ElementCount &&
        shardKey.Elements.Zip(keys.Elements).All(pair => pair.First.Name == pair.Second.Name && StoredSpecs.Same(pair.First.Value, pair.Second.Value));

    /// <summary>The index as the server lists it, without its version: what creates it again, shown in a log.</summary>
    public static string DefinitionOf(BsonDocument listed) => new BsonDocument(listed.Where(element => element.Name != "v")).ToJson();

    /// <summary>An index's name, as the server lists it.</summary>
    public static string? NameOf(BsonDocument listed) => listed.GetValue("name", BsonNull.Value) is BsonString name ? name.Value : null;

    // The server's own: _id_, and Queryable Encryption's on __safeContent__.
    private static bool IsServers(BsonDocument listed) =>
        NameOf(listed) == "_id_" ||
        listed.GetValue("key", BsonNull.Value) is BsonDocument { ElementCount: 1 } key && key.GetElement(0).Name == "__safeContent__";

    // The collation the index compares strings with: its own, or the collection's.
    private static BsonDocument? Collation(IndexDeclaration declared,
        BsonDocument? collation) =>
        declared.Collation is null ? collation : StoredSpecs.Collation(declared.Collation);

    private static bool Flag(BsonDocument listed,
        string name) =>
        listed.TryGetValue(name, out var value) && value.ToBoolean();

    // Text keys, as the server lists them: one _fts and _ftsx pair where the first of them was, with their fields as weights.
    private static (BsonDocument Keys, BsonDocument? Weights) ListedKeys(BsonDocument keys)
    {
        var listed = new BsonDocument();
        BsonDocument? weights = null;

        foreach (var key in keys)
        {
            if (key.Value is not BsonString { Value: "text" })
            {
                listed.Add(key);
                continue;
            }

            if (weights is null)
            {
                weights = [];
                listed.Add("_fts", "text").Add("_ftsx", 1);
            }

            weights[key.Name] = 1;
        }

        return (listed, weights);
    }
}

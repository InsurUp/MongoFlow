using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// Compares a collection's declared settings with what <c>listCollections</c> lists of it: what MongoDB can't change in
/// place, its kind, collation and clustering, and what it can, its validator, change stream images and expiry.
/// </summary>
internal static class CollectionSettingsSpecs
{
    /// <summary>What differs between <paramref name="declared"/> and <paramref name="listed"/>, the collection as listed.</summary>
    public static SpecComparison Compare(CollectionSettings declared,
        BsonDocument listed)
    {
        var conflicts = new List<string>();
        var options = listed.GetValue("options", new BsonDocument()).AsBsonDocument;

        if (listed.GetValue("type", "collection").AsString is var type and not "collection")
        {
            conflicts.Add($"it's a {type}, not a collection");
        }

        if (declared.Collation is not null)
        {
            var expected = StoredSpecs.Collation(declared.Collation);
            var stored = options.GetValue("collation", BsonNull.Value) as BsonDocument;
            if (!StoredSpecs.SameCollation(expected, stored))
            {
                conflicts.Add($"it compares strings with {StoredSpecs.Describe(stored)}, not {StoredSpecs.Describe(expected)}");
            }
        }

        var clustered = options.Contains("clusteredIndex");
        if (declared.Clustered && !clustered)
        {
            conflicts.Add("it isn't clustered, and is declared clustered");
        }

        var changes = new BsonDocument();
        if (declared.Validator is not null)
        {
            if (!StoredSpecs.Same(options.GetValue("validator", BsonNull.Value), declared.Validator))
            {
                changes.Add("validator", declared.Validator);
            }

            if (options.GetValue("validationLevel", "strict").AsString != declared.Level)
            {
                changes.Add("validationLevel", declared.Level);
            }

            if (options.GetValue("validationAction", "error").AsString != declared.Action)
            {
                changes.Add("validationAction", declared.Action);
            }
        }

        if (declared.ChangeStreamPreAndPostImages is { } images &&
            images != (options.GetValue("changeStreamPreAndPostImages", BsonNull.Value) is BsonDocument storedImages &&
                       storedImages.GetValue("enabled", false).ToBoolean()))
        {
            changes.Add("changeStreamPreAndPostImages", new BsonDocument("enabled", images));
        }

        if (declared.Clustered && clustered)
        {
            var expiry = options.GetValue("expireAfterSeconds", BsonNull.Value);
            BsonValue expected = declared.ExpireAfter is { } expireAfter ? (long)expireAfter.TotalSeconds : BsonNull.Value;
            if (!StoredSpecs.Same(expiry, expected))
            {
                changes.Add("expireAfterSeconds", expected.IsBsonNull ? "off" : expected);
            }
        }

        return new SpecComparison(conflicts, changes.ElementCount > 0 ? changes : null);
    }
}

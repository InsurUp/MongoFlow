using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Collections;

/// <summary>
/// A collection's declared settings compared with what <c>listCollections</c> lists, as captured from MongoDB 8.2:
/// <list type="number">
/// <item>one listed as declared is left, and a setting not declared isn't compared;</item>
/// <item>its validator, validation level and action, change stream images and expiry are changed in place, in one
/// <c>collMod</c>, an expiry turned off included;</item>
/// <item>its collation, a clustering it lacks, and a kind other than a collection conflict.</item>
/// </list>
/// </summary>
public class CollectionSettingsSpecsTests
{
    private const string Turkish =
        "{ locale: 'tr', caseLevel: false, caseFirst: 'off', strength: 2, numericOrdering: false, alternate: 'non-ignorable', " +
        "maxVariable: 'punct', normalization: false, backwards: false, version: '57.1' }";

    [Test]
    [MethodDataSource(nameof(Unchanged))]
    public async Task Compare_ListedAsDeclared_FindsNothing(string name,
        string declared,
        string listed)
    {
        // Act
        var comparison = CollectionSettingsSpecs.Compare(Declared(declared), BsonDocument.Parse(listed));

        // Assert
        await Verify(comparison).DontIgnoreEmptyCollections().UseParameters(name);
    }

    [Test]
    [MethodDataSource(nameof(InPlace))]
    public async Task Compare_WhatChangesInPlaceDiffers_ChangesItInOneCollMod(string name,
        string declared,
        string listed)
    {
        // Act
        var comparison = CollectionSettingsSpecs.Compare(Declared(declared), BsonDocument.Parse(listed));

        // Assert
        await Verify(comparison).UseParameters(name);
    }

    [Test]
    [MethodDataSource(nameof(Conflicting))]
    public async Task Compare_DiffersInWhatCantChangeInPlace_Conflicts(string name,
        string declared,
        string listed)
    {
        // Act
        var comparison = CollectionSettingsSpecs.Compare(Declared(declared), BsonDocument.Parse(listed));

        // Assert
        await Verify(comparison).UseParameters(name);
    }

    public static IEnumerable<Func<(string, string, string)>> Unchanged()
    {
        yield return () => ("collation, as the server fills it in", "{ collation: { locale: 'tr', strength: 2 } }",
            $"{{ name: 'c', type: 'collection', options: {{ collation: {Turkish} }} }}");
        yield return () => ("validator, at the default level and action", "{ validator: { total: { $gte: 0 } } }",
            "{ name: 'c', type: 'collection', options: { validator: { total: { $gte: 0 } } } }");
        yield return () => ("validator, warning", "{ validator: { total: { $gte: 0 } }, validationLevel: 'Moderate', validationAction: 'Warn' }",
            "{ name: 'c', type: 'collection', options: { validator: { total: { $gte: 0 } }, validationLevel: 'moderate', validationAction: 'warn' } }");
        yield return () => ("change stream images", "{ changeStreamPreAndPostImages: true }",
            "{ name: 'c', type: 'collection', options: { changeStreamPreAndPostImages: { enabled: true } } }");
        yield return () => ("no change stream images", "{ changeStreamPreAndPostImages: false }", "{ name: 'c', type: 'collection', options: {} }");
        yield return () => ("clustered, expiring", "{ clustered: true, expireAfterSeconds: 3600 }",
            "{ name: 'c', type: 'collection', options: { clusteredIndex: { v: 2, key: { _id: 1 }, name: '_id_', unique: true }, expireAfterSeconds: 3600 } }");
        yield return () => ("what isn't declared", "{}",
            $"{{ name: 'c', type: 'collection', options: {{ collation: {Turkish}, validator: {{ a: 1 }}, clusteredIndex: {{ key: {{ _id: 1 }} }} }} }}");
    }

    public static IEnumerable<Func<(string, string, string)>> InPlace()
    {
        yield return () => ("validator added", "{ validator: { total: { $gte: 0 } } }", "{ name: 'c', type: 'collection', options: {} }");
        yield return () => ("validator, level and action changed",
            "{ validator: { total: { $gte: 0 } }, validationLevel: 'Moderate', validationAction: 'Warn' }",
            "{ name: 'c', type: 'collection', options: { validator: { total: { $gt: 0 } }, validationLevel: 'strict', validationAction: 'error' } }");
        yield return () => ("change stream images switched off", "{ changeStreamPreAndPostImages: false }",
            "{ name: 'c', type: 'collection', options: { changeStreamPreAndPostImages: { enabled: true } } }");
        yield return () => ("expiry changed", "{ clustered: true, expireAfterSeconds: 60 }",
            "{ name: 'c', type: 'collection', options: { clusteredIndex: { key: { _id: 1 } }, expireAfterSeconds: 3600 } }");
        yield return () => ("expiry turned off", "{ clustered: true }",
            "{ name: 'c', type: 'collection', options: { clusteredIndex: { key: { _id: 1 } }, expireAfterSeconds: 3600 } }");
    }

    public static IEnumerable<Func<(string, string, string)>> Conflicting()
    {
        yield return () => ("collation", "{ collation: { locale: 'en' } }", $"{{ name: 'c', type: 'collection', options: {{ collation: {Turkish} }} }}");
        yield return () => ("collation, on one without", "{ collation: { locale: 'tr' } }", "{ name: 'c', type: 'collection', options: {} }");
        yield return () => ("clustered, on one that isn't", "{ clustered: true }", "{ name: 'c', type: 'collection', options: {} }");
        yield return () => ("a view", "{ changeStreamPreAndPostImages: false }", "{ name: 'c', type: 'view', options: { viewOn: 'other', pipeline: [] } }");
    }

    // Settings written as a document of what's declared.
    private static CollectionSettings Declared(string json)
    {
        var settings = BsonDocument.Parse(json);
        return new CollectionSettings(
            settings.GetValue("collation", BsonNull.Value) is BsonDocument collation ? Collation.FromBsonDocument(collation) : null,
            settings.GetValue("validator", BsonNull.Value) as BsonDocument,
            Enum.Parse<DocumentValidationLevel>(settings.GetValue("validationLevel", "Strict").AsString),
            Enum.Parse<DocumentValidationAction>(settings.GetValue("validationAction", "Error").AsString),
            settings.TryGetValue("changeStreamPreAndPostImages", out var images) ? images.ToBoolean() : null,
            settings.GetValue("clustered", false).ToBoolean(),
            settings.TryGetValue("expireAfterSeconds", out var seconds) ? TimeSpan.FromSeconds(seconds.ToInt64()) : null);
    }
}

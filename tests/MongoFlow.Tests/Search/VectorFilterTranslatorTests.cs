using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="VectorFilterTranslator"/> splits a rendered query filter for a vector search:
/// <list type="number">
/// <item>comparisons of the index's filter fields with plain values stay MQL, in the search's filter;</item>
/// <item>a null compared for equality matches an explicit null or a missing field, and <c>$ne: null</c> a field present
/// and not null, as in MQL, which the vector filter's null alone doesn't;</item>
/// <item><c>$or</c> and <c>$nor</c> stay only when every branch does, and <c>$and</c> is flattened;</item>
/// <item>anything else, and any field that isn't a filter field, stays behind to match after the search.</item>
/// </list>
/// </summary>
public class VectorFilterTranslatorTests
{
    private static readonly SearchIndexMapping Mapping = SearchIndexMapping.Parse(BsonDocument.Parse("""
        { fields: [
            { type: 'vector', path: 'Embedding', numDimensions: 3, similarity: 'cosine' },
            { type: 'filter', path: 'TenantId' },
            { type: 'filter', path: 'IsDeleted' },
            { type: 'filter', path: 'Total' },
            { type: 'filter', path: 'Reference' }] }
        """), SearchIndexType.VectorSearch);

    [Test]
    [MethodDataSource(nameof(QueryFilters))]
    public async Task Split_QueryFilter_FiltersWhatTheIndexHasFilterFieldsFor(string filter,
        BsonDocument rendered)
    {
        // Act
        var split = VectorFilterTranslator.Split(rendered, Mapping);

        // Assert
        await Verify(split).UseParameters(filter);
    }

    public static IEnumerable<Func<(string, BsonDocument)>> QueryFilters()
    {
        yield return () => ("equality", Parse("{ TenantId: 't-1', IsDeleted: { $eq: false } }"));
        yield return () => ("null", Parse("{ TenantId: null }"));
        yield return () => ("ne null", Parse("{ TenantId: { $ne: null } }"));
        yield return () => ("ne", Parse("{ IsDeleted: { $ne: true } }"));
        yield return () => ("range", Parse("{ Total: { $gte: 10, $lt: 100 } }"));
        yield return () => ("in", Parse("{ TenantId: { $in: ['t-1', 't-2'] }, Reference: { $nin: [UUID('3a1f2b9e-0000-4000-8000-000000000001')] } }"));
        yield return () => ("in with null", Parse("{ TenantId: { $in: ['t-1', null] } }"));
        yield return () => ("exists", Parse("{ TenantId: { $exists: false } }"));
        yield return () => ("not", Parse("{ Total: { $not: { $gt: 5 } } }"));
        yield return () => ("or", Parse("{ $or: [{ TenantId: 't-1' }, { IsDeleted: true }] }"));
        yield return () => ("nor partly filter fields", Parse("{ $nor: [{ TenantId: 't-1' }, { Customer: 'ada' }] }"));
        yield return () => ("and", Parse("{ $and: [{ Total: { $gt: 1 } }, { Total: { $lt: 5 } }] }"));
        yield return () => ("unmapped", Parse("{ Customer: 'ada', TenantId: 't-1' }"));
        yield return () => ("regex", Parse("{ TenantId: { $regex: '^t' } }"));
        yield return () => ("document value", Parse("{ TenantId: { Id: 1 } }"));
        yield return () => ("expr", Parse("{ $expr: { $eq: ['$TenantId', 't-1'] } }"));
        yield return () => ("empty or", Parse("{ $or: [] }"));
    }

    private static BsonDocument Parse(string json) => BsonDocument.Parse(json);
}

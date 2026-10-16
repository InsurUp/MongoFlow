using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Tests.Search;

/// <summary>
/// <see cref="SearchFilterTranslator"/> splits a rendered query filter for a search:
/// <list type="number">
/// <item>equality, inequality, ranges, sets, existence and negation of fields the index maps to compare exactly become
/// <c>filter</c> and <c>mustNot</c> clauses that match what MQL does, null included;</item>
/// <item><c>$or</c> and <c>$nor</c> become clauses only when every branch does, and <c>$and</c> is flattened;</item>
/// <item>anything else, and any field the index doesn't map to compare, such as analyzed text, a lowercased token or an
/// embedded document, stays behind to match after the search, operator by operator, with the fields it compares named.</item>
/// </list>
/// </summary>
public class SearchFilterTranslatorTests
{
    private static readonly SearchIndexMapping Mapping = SearchIndexMapping.Parse(BsonDocument.Parse("""
        { mappings: { dynamic: false, fields: {
            TenantId: { type: 'token' },
            IsDeleted: { type: 'boolean' },
            Total: { type: 'number' },
            Count: { type: 'number', representation: 'int64' },
            PlacedAt: { type: 'date' },
            OwnerId: { type: 'objectId' },
            Reference: { type: 'uuid' },
            Customer: { type: 'string' },
            Email: { type: 'token', normalizer: 'lowercase' },
            Address: { type: 'document', fields: { City: { type: 'token' } } },
            Lines: { type: 'embeddedDocuments', fields: { Sku: { type: 'token' } } } } } }
        """), SearchIndexType.Search);

    [Test]
    [MethodDataSource(nameof(QueryFilters))]
    public async Task Split_QueryFilter_FiltersWhatTheIndexMapsAndLeavesTheRest(string filter,
        BsonDocument rendered)
    {
        // Act
        var split = SearchFilterTranslator.Split(rendered, Mapping);

        // Assert
        await Verify(split).UseParameters(filter);
    }

    [Test]
    public async Task Split_EmptyFilter_FiltersNothing()
    {
        // Act
        var split = SearchFilterTranslator.Split([], Mapping);

        // Assert
        await Assert.That(split.HasClauses || split.Residual is not null).IsFalse();
    }

    public static IEnumerable<Func<(string, BsonDocument)>> QueryFilters()
    {
        yield return () => ("equality", Parse("{ TenantId: 't-1' }"));
        yield return () => ("eq", Parse("{ TenantId: { $eq: 't-1' } }"));
        yield return () => ("ne", Parse("{ IsDeleted: { $ne: true } }"));
        yield return () => ("null", Parse("{ TenantId: null }"));
        yield return () => ("ne null", Parse("{ TenantId: { $ne: null } }"));
        yield return () => ("in", Parse("{ TenantId: { $in: ['t-1', 't-2'] } }"));
        yield return () => ("in numbers", Parse("{ Total: { $in: [1, 2.5, NumberLong(3)] } }"));
        yield return () => ("in with null", Parse("{ TenantId: { $in: ['t-1', null] } }"));
        yield return () => ("in of kinds", Parse("{ OwnerId: { $in: [ObjectId('650000000000000000000001'), null] }, Total: { $in: [1, 'one'] } }"));
        yield return () => ("nin", Parse("{ TenantId: { $nin: ['t-1', 't-2'] } }"));
        yield return () => ("range", Parse("{ Total: { $gte: 10, $lt: 100 } }"));
        yield return () => ("date range", Parse("{ PlacedAt: { $gt: ISODate('2026-01-01T00:00:00Z') } }"));
        yield return () => ("exists", Parse("{ TenantId: { $exists: true }, IsDeleted: { $exists: 0 } }"));
        yield return () => ("not", Parse("{ Total: { $not: { $gt: 5 } } }"));
        yield return () => ("or", Parse("{ $or: [{ TenantId: 't-1' }, { IsDeleted: true, Total: 3 }] }"));
        yield return () => ("or of one", Parse("{ $or: [{ TenantId: 't-1' }] }"));
        yield return () => ("or partly mapped", Parse("{ $or: [{ TenantId: 't-1' }, { Customer: 'ada' }] }"));
        yield return () => ("nor", Parse("{ $nor: [{ IsDeleted: true }] }"));
        yield return () => ("and", Parse("{ $and: [{ TenantId: 't-1' }, { $and: [{ IsDeleted: { $ne: true } }] }] }"));
        yield return () => ("values", Parse("{ OwnerId: ObjectId('650000000000000000000001'), Reference: UUID('3a1f2b9e-0000-4000-8000-000000000001'), IsDeleted: false }"));
        yield return () => ("nested document", Parse("{ 'Address.City': 'Ankara' }"));
        yield return () => ("analyzed text", Parse("{ Customer: 'ada' }"));
        yield return () => ("lowercased token", Parse("{ Email: 'Ada@Example.com' }"));
        yield return () => ("embedded documents", Parse("{ 'Lines.Sku': 'S-1' }"));
        yield return () => ("unmapped", Parse("{ Unmapped: 1, Total: { $gt: 1 } }"));
        yield return () => ("int64 beyond doubles", Parse("{ Total: NumberLong('9007199254740993'), Count: NumberLong('9007199254740993') }"));
        yield return () => ("decimal", Parse("{ Total: NumberDecimal('1.5') }"));
        yield return () => ("partly translated field", Parse("{ Total: { $gt: 1, $mod: [2, 0] } }"));
        yield return () => ("regex", Parse("{ TenantId: { $regex: '^t', $options: 'i' } }"));
        yield return () => ("expr", Parse("{ $expr: { $gt: ['$Total', '$Count'] }, TenantId: 't-1' }"));
        yield return () => ("array", Parse("{ TenantId: ['t-1'], Total: { $in: [[1]] } }"));
        yield return () => ("null range", Parse("{ Total: { $gte: null } }"));
        yield return () => ("not of a regex", Parse("{ TenantId: { $not: /^t/ } }"));
        yield return () => ("residual twice", Parse("{ $and: [{ Customer: 'ada' }, { Customer: { $ne: 'bob' } }] }"));
        yield return () => ("empty or", Parse("{ $or: [] }"));
    }

    private static BsonDocument Parse(string json) => BsonDocument.Parse(json);
}

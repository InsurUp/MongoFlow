using System.Linq.Expressions;
using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// Declares a collection's search index, which the migrator creates, keeps up to date and waits for. MongoFlow adds the
/// fields its features filter on, such as the tenant and soft delete's flag, so searches filter on them in the index.
/// </summary>
public interface ISearchIndexBuilder<TDocument>
{
    /// <summary>
    /// The definition to start from, as MongoDB Search takes it, such as one kept in JSON. What the other calls declare is
    /// added to it.
    /// </summary>
    ISearchIndexBuilder<TDocument> Definition(BsonDocument definition);

    /// <summary>The analyzer text fields are indexed with, unless they name their own, such as <c>lucene.turkish</c>.</summary>
    ISearchIndexBuilder<TDocument> Analyzer(string analyzer);

    /// <summary>The analyzer text queries are analyzed with; <see cref="Analyzer"/> without.</summary>
    ISearchIndexBuilder<TDocument> SearchAnalyzer(string analyzer);

    /// <summary>
    /// Whether fields that aren't mapped are indexed too, by their type; strings as analyzed text, which equality can't
    /// match. Off unless called, or set in <see cref="Definition"/>.
    /// </summary>
    ISearchIndexBuilder<TDocument> Dynamic(bool dynamic = true);

    /// <summary>
    /// Whether the documents <c>Map</c>'s dotted paths create, such as <c>Address</c> for <c>c =&gt; c.Address.City</c>,
    /// index their fields that aren't mapped too, as <see cref="Dynamic"/> does at the index's root: off for documents that
    /// index only the fields mapped in them while the root is dynamic. As the index's own setting unless called. A document
    /// mapped already, in <see cref="Definition"/> or as <see cref="SearchField.Raw"/>, keeps its own.
    /// </summary>
    ISearchIndexBuilder<TDocument> DocumentsDynamic(bool dynamic = true);

    /// <summary>Maps <paramref name="member"/>, such as <c>c =&gt; c.Name</c> or <c>c =&gt; c.Address.City</c>, as <paramref name="fields"/>.</summary>
    ISearchIndexBuilder<TDocument> Map<TMember>(Expression<Func<TDocument, TMember>> member,
        params SearchField[] fields);

    /// <summary>Maps the field at <paramref name="path"/>, such as <c>Address.City</c>, as <paramref name="fields"/>.</summary>
    ISearchIndexBuilder<TDocument> Map(string path,
        params SearchField[] fields);
}

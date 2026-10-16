using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Declares a collection's vector search index, which the migrator creates, keeps up to date and waits for. MongoFlow
/// adds the fields its features filter on as filter fields, so vector searches filter on them in the index.
/// </summary>
public interface IVectorSearchIndexBuilder<TDocument>
{
    /// <summary>The definition to start from, as MongoDB Vector Search takes it. What the other calls declare is added to it.</summary>
    IVectorSearchIndexBuilder<TDocument> Definition(BsonDocument definition);

    /// <summary>Indexes the vectors <paramref name="member"/> holds, of <paramref name="dimensions"/> numbers each.</summary>
    /// <param name="member">The member holding the embedding, such as <c>c =&gt; c.Embedding</c>.</param>
    /// <param name="dimensions">How many numbers each vector holds, from 1 to 8192.</param>
    /// <param name="similarity">How closeness is measured.</param>
    /// <param name="quantization">How the vectors are compressed in the index; not at all without.</param>
    IVectorSearchIndexBuilder<TDocument> Vector<TMember>(Expression<Func<TDocument, TMember>> member,
        int dimensions,
        VectorSimilarity similarity,
        VectorQuantization? quantization = null);

    /// <summary>Indexes <paramref name="member"/> for a vector search's filter to compare.</summary>
    IVectorSearchIndexBuilder<TDocument> Filter<TMember>(Expression<Func<TDocument, TMember>> member);

    /// <summary>Indexes the field at <paramref name="path"/> for a vector search's filter to compare.</summary>
    IVectorSearchIndexBuilder<TDocument> Filter(string path);
}

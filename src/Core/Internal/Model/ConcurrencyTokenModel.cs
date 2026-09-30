using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

internal abstract class ConcurrencyTokenModel<TDocument>
{
    /// <summary>A filter matching the stored document only if its token still has the value <paramref name="document"/> holds.</summary>
    public abstract Expression<Func<TDocument, bool>> Matches(TDocument document);

    /// <summary>Increments the token on <paramref name="document"/>, returning how to undo it if the save fails.</summary>
    public abstract Action Increment(TDocument document);

    public abstract UpdateDefinition<TDocument> WithIncrement(UpdateDefinition<TDocument> update);
}

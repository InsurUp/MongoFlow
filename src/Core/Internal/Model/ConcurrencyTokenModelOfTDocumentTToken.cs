using System.Linq.Expressions;
using System.Numerics;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed class ConcurrencyTokenModel<TDocument, TToken> : ConcurrencyTokenModel<TDocument>
    where TToken : INumber<TToken>
{
    private readonly Expression<Func<TDocument, TToken>> _token;
    private readonly Func<TDocument, TToken> _get;
    private readonly Action<TDocument, TToken> _set;

    /// <param name="token">The token member, read from <typeparamref name="TDocument"/>, for filters and updates.</param>
    /// <param name="get">Reads the token from a document.</param>
    /// <param name="set">Writes the token to a document.</param>
    public ConcurrencyTokenModel(Expression<Func<TDocument, TToken>> token,
        Func<TDocument, TToken> get,
        Action<TDocument, TToken> set)
    {
        _token = token;
        _get = get;
        _set = set;
    }

    public override Expression<Func<TDocument, bool>> Matches(TDocument document) =>
        Expression.Lambda<Func<TDocument, bool>>(
            Expression.Equal(_token.Body, Expression.Constant(_get(document), typeof(TToken))),
            _token.Parameters);

    public override Action Increment(TDocument document)
    {
        var current = _get(document);
        _set(document, current + TToken.One);

        return () => _set(document, current);
    }

    public override UpdateDefinition<TDocument> WithIncrement(UpdateDefinition<TDocument> update) =>
        Builders<TDocument>.Update.Combine(update, Builders<TDocument>.Update.Inc(_token, TToken.One));
}

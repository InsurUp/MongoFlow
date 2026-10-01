using System.Linq.Expressions;
using System.Numerics;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Limits each write made with a document to the stored document whose token still has the value that was read, and
/// increments the token: in storage, and on the document so the two stay in step.
/// </summary>
internal sealed class ConcurrencyTokenInterceptor<TDocument, TVersioned, TToken>(
    Expression<Func<TVersioned, TToken>> token,
    Func<TVersioned, TToken> getToken,
    Action<TVersioned, TToken> setToken) : VaultInterceptor
    where TToken : INumber<TToken>
{
    private readonly Expression<Func<TDocument, TToken>> _field = MemberExpressions.Rebind<TVersioned, TDocument, TToken>(token);

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var incremented = new List<(TVersioned Document, TToken Read)>();

        foreach (var operation in context.Operations)
        {
            switch (operation)
            {
                case ReplaceOperation<TDocument> replace:
                    Guard(replace, replace.Document!, incremented);
                    break;

                case UpdateOperation<TDocument> update:
                    if (update.Document is { } document)
                    {
                        Guard(update, document, incremented);
                    }

                    context.Replace(update, update.WithUpdate(Builders<TDocument>.Update.Combine(
                        update.Update,
                        Builders<TDocument>.Update.Inc(_field, TToken.One))));
                    break;

                case DeleteOperation<TDocument> { Document: { } deleted } delete:
                    delete.Condition = Matches(deleted);
                    break;
            }
        }

        if (incremented.Count > 0)
        {
            // The interceptor is shared by every request, so what to put back is kept with the save.
            context.Items[this] = incremented;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Fails the save when a guarded write matched nothing: the stored document changed, or is gone. It runs before the
    /// other interceptors' <see cref="VaultInterceptor.SavedAsync"/>, so none of them sees the rejected write.
    /// </summary>
    public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var operation in context.Operations)
        {
            if (operation is VaultOperation<TDocument> { Condition: not null, Result: { Matched: 0, Deleted: 0 } } guarded)
            {
                throw new ConcurrencyException(guarded,
                    await guarded.TypedModel.TargetExistsAsync(guarded, context.Run, cancellationToken));
            }
        }
    }

    /// <summary>Puts back the tokens incremented on documents, since their writes were rolled back.</summary>
    public override ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.Items.TryGetValue(this, out var value) && value is List<(TVersioned Document, TToken Read)> incremented)
        {
            context.Items.Remove(this);

            // Newest first, so a document incremented twice in one save ends at the value first read.
            for (var i = incremented.Count - 1; i >= 0; i--)
            {
                setToken(incremented[i].Document, incremented[i].Read);
            }
        }

        return ValueTask.CompletedTask;
    }

    private void Guard(VaultOperation<TDocument> operation,
        TDocument document,
        List<(TVersioned Document, TToken Read)> incremented)
    {
        var versioned = (TVersioned)(object)document!;
        var read = getToken(versioned);

        operation.Condition = Matches(document);
        setToken(versioned, read + TToken.One);
        incremented.Add((versioned, read));
    }

    private Expression<Func<TDocument, bool>> Matches(TDocument document) =>
        Expression.Lambda<Func<TDocument, bool>>(
            Expression.Equal(_field.Body, Expression.Constant(getToken((TVersioned)(object)document!), typeof(TToken))),
            _field.Parameters);
}

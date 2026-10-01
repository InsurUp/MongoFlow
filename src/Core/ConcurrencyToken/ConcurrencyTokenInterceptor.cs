using System.Linq.Expressions;
using System.Numerics;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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

    // Rendered on first use, with the collection's serializers. Two requests may both render them; they get the same.
    private RenderedFieldDefinition? _renderedField;
    private BsonValue? _one;

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

                    update.Update = Incremented(update);
                    break;

                case DeleteOperation<TDocument> { Document: { } deleted } delete:
                    delete.Condition = Matches(delete, deleted);
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
                var exists = await guarded.TargetExistsAsync(context.Run, cancellationToken);

                context.Run.Runtime.Model.Logs.Save.ConcurrencyConflict(guarded.Namespace.CollectionName, guarded.TargetKey, exists);
                throw new ConcurrencyException(guarded, exists);
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

        operation.Condition = Matches(operation, document);
        setToken(versioned, read + TToken.One);
        incremented.Add((versioned, read));
    }

    /// <summary><c>{ Version: 3 }</c>: the stored token still has the value <paramref name="document"/> was read with.</summary>
    private BsonDocument Matches(VaultOperation<TDocument> operation,
        TDocument document) =>
        new BsonDocument().Add(FilterDocuments.Equal(Field(operation), getToken((TVersioned)(object)document!)));

    /// <summary>
    /// The update with <c>{ $inc: { Version: 1 } }</c> added, as BSON, so the driver doesn't translate the token's member
    /// for every update.
    /// </summary>
    private IncrementedUpdateDefinition<TDocument> Incremented(UpdateOperation<TDocument> operation)
    {
        var field = Field(operation);

        return new IncrementedUpdateDefinition<TDocument>(operation.Update, field.FieldName,
            _one ??= field.FieldSerializer.ToBsonValue(TToken.One));
    }

    private RenderedFieldDefinition Field(VaultOperation<TDocument> operation) =>
        _renderedField ??= FilterDocuments.RenderField(_field, operation.TypedModel.RenderArgs);
}

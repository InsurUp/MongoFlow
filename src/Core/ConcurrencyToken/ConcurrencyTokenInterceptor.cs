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
        var guards = new List<TokenGuard<TDocument, TVersioned, TToken>>();

        foreach (var operation in context.Operations)
        {
            switch (operation)
            {
                case ReplaceOperation<TDocument> replace:
                    Guard(replace, replace.Document!, increment: true, guards);
                    break;

                case UpdateOperation<TDocument> update:
                    if (update.Document is { } document)
                    {
                        Guard(update, document, increment: true, guards);
                    }

                    update.Update = Incremented(update);
                    break;

                case DeleteOperation<TDocument> { Document: { } deleted } delete:
                    Guard(delete, deleted, increment: false, guards);
                    break;
            }
        }

        if (guards.Count > 0)
        {
            // The interceptor is shared by every request, so what it guarded is kept with the save.
            context.Items[this] = guards;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Fails the save when a guarded write matched nothing because of the token: the stored document changed, or is gone.
    /// It runs before the other interceptors' <see cref="VaultInterceptor.SavedAsync"/>, so none of them sees the rejected
    /// write.
    /// </summary>
    /// <remarks>
    /// A write can carry other interceptors' conditions too. When the token's held, one of theirs failed: the save goes on
    /// for them to decide, and the document's token is put back, since nothing was written.
    /// </remarks>
    public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue(this, out var value) || value is not List<TokenGuard<TDocument, TVersioned, TToken>> guards)
        {
            return;
        }

        foreach (var guard in guards)
        {
            var operation = guard.Operation;
            if (operation.Result is not { Matched: 0, Deleted: 0 })
            {
                continue;
            }

            if (await operation.TargetExistsAsync(context.Run, guard.Condition, cancellationToken))
            {
                if (guard.Incremented)
                {
                    setToken(guard.Document, guard.Read);
                }

                continue;
            }

            var exists = await operation.TargetExistsAsync(context.Run, condition: null, cancellationToken);

            context.Run.Runtime.Model.Logs.Save.ConcurrencyConflict(operation.Namespace.CollectionName, operation.TargetKey, exists);
            throw new ConcurrencyException(operation, exists);
        }
    }

    /// <summary>Puts back the tokens incremented on documents, since their writes were rolled back.</summary>
    public override ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.Items.TryGetValue(this, out var value) && value is List<TokenGuard<TDocument, TVersioned, TToken>> guards)
        {
            context.Items.Remove(this);

            // Newest first, so a document incremented twice in one save ends at the value first read.
            for (var i = guards.Count - 1; i >= 0; i--)
            {
                if (guards[i].Incremented)
                {
                    setToken(guards[i].Document, guards[i].Read);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Limits the write to the stored document whose token has the value <paramref name="document"/> was read with, and
    /// increments the document's token if <paramref name="increment"/>.
    /// </summary>
    private void Guard(VaultOperation<TDocument> operation,
        TDocument document,
        bool increment,
        List<TokenGuard<TDocument, TVersioned, TToken>> guards)
    {
        var versioned = (TVersioned)(object)document!;
        var read = getToken(versioned);
        var condition = Matches(operation, read);

        operation.AddCondition(condition);
        if (increment)
        {
            setToken(versioned, read + TToken.One);
        }

        guards.Add(new TokenGuard<TDocument, TVersioned, TToken>(operation, condition, versioned, read, increment));
    }

    /// <summary><c>{ Version: 3 }</c>: the stored token still has the value that was read.</summary>
    private BsonDocument Matches(VaultOperation<TDocument> operation,
        TToken read) =>
        new BsonDocument().Add(FilterDocuments.Equal(Field(operation), read));

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

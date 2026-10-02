using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow.Identity;

/// <summary>
/// Guards writes of users and roles with their <c>ConcurrencyStamp</c>, as Identity's own stores do: a replace or delete
/// applies only while the stored stamp is the one read, and a replace gives the document a new stamp, put back if the save
/// fails. A write that finds the stamp changed, or the document gone, fails the save with
/// <see cref="ConcurrencyStampConflictException"/>.
/// </summary>
/// <remarks>
/// Writes are matched to their documents, not to their operations, since soft delete may put an update in a delete's place.
/// </remarks>
internal sealed class ConcurrencyStampGuard<TDocument> : VaultInterceptor where TDocument : class
{
    private readonly Expression<Func<TDocument, string?>> _stamp;
    private readonly Func<TDocument, string?> _get;
    private readonly Action<TDocument, string?> _set;

    public ConcurrencyStampGuard()
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var stamp = Expression.Property(document, "ConcurrencyStamp");
        var value = Expression.Parameter(typeof(string), "value");

        _stamp = Expression.Lambda<Func<TDocument, string?>>(stamp, document);
        _get = _stamp.Compile();
        _set = Expression.Lambda<Action<TDocument, string?>>(Expression.Assign(stamp, value), document, value).Compile();
    }

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        List<(TDocument Document, string? Read, bool Renewed)>? guarded = null;

        foreach (var operation in context.Operations)
        {
            var renew = operation is ReplaceOperation<TDocument>;
            if (operation is not (ReplaceOperation<TDocument> or DeleteOperation<TDocument> or UpdateOperation<TDocument>) ||
                operation.IsSetBased || operation.Document is not TDocument document)
            {
                continue;
            }

            var read = _get(document);
            ((VaultOperation<TDocument>)operation).AddCondition(Builders<TDocument>.Filter.Eq(_stamp, read));
            if (renew)
            {
                _set(document, Guid.NewGuid().ToString());
            }

            (guarded ??= []).Add((document, read, renew));
        }

        if (guarded is not null)
        {
            // The interceptor is shared by every request, so what it guarded is kept with the save.
            context.Items[this] = guarded;
        }

        return ValueTask.CompletedTask;
    }

    public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue(this, out var items))
        {
            return ValueTask.CompletedTask;
        }

        var guarded = (List<(TDocument Document, string? Read, bool Renewed)>)items!;

        foreach (var operation in context.Operations)
        {
            if (operation is { Result: { Matched: 0, Deleted: 0 }, Document: TDocument document } &&
                guarded.Exists(entry => ReferenceEquals(entry.Document, document)))
            {
                throw new ConcurrencyStampConflictException(typeof(TDocument));
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Puts back the stamps renewed on documents, newest first, since their writes were rolled back.</summary>
    public override ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.Items.Remove(this, out var items))
        {
            var guarded = (List<(TDocument Document, string? Read, bool Renewed)>)items!;
            for (var i = guarded.Count - 1; i >= 0; i--)
            {
                if (guarded[i].Renewed)
                {
                    _set(guarded[i].Document, guarded[i].Read);
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}

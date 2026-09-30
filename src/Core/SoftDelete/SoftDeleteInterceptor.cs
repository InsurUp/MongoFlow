using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Turns each delete into an update that marks the document deleted, and marks the deleted document too.</summary>
internal sealed class SoftDeleteInterceptor<TDocument, TSoftDelete, TValue>(
    Expression<Func<TSoftDelete, TValue>> member,
    Action<TSoftDelete, TValue> setMember,
    Func<IServiceProvider, TValue> deletedValue) : VaultInterceptor
{
    private readonly Expression<Func<TDocument, TValue>> _field = MemberExpressions.Rebind<TSoftDelete, TDocument, TValue>(member);

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var deletes = context.Operations.OfType<DeleteOperation<TDocument>>().ToList();
        if (deletes.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var value = deletedValue(context.Services);
        var markDeleted = Builders<TDocument>.Update.Set(_field, value);

        foreach (var delete in deletes)
        {
            if (delete.Document is TSoftDelete document)
            {
                setMember(document, value);
            }

            context.Replace(delete, delete.ToUpdate(markDeleted));
        }

        return ValueTask.CompletedTask;
    }
}

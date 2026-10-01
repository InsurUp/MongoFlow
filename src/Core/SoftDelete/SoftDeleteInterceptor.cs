using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Turns each delete into an update that marks the document deleted, and marks the deleted document too.</summary>
internal sealed class SoftDeleteInterceptor<TDocument, TSoftDelete, TValue>(
    Expression<Func<TSoftDelete, TValue>> member,
    Action<TSoftDelete, TValue> setMember,
    Func<IServiceProvider, TValue> deletedValue) : VaultInterceptor
{
    private readonly Expression<Func<TDocument, TValue>> _field = MemberExpressions.Rebind<TSoftDelete, TDocument, TValue>(member);

    // Rendered on first use, with the collection's serializers. Two requests may both render it; they get the same.
    private RenderedFieldDefinition? _renderedField;

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        // Decided once per save that deletes, and serialized once, so the driver doesn't translate the member per delete.
        TValue value = default!;
        BsonValue? stored = null;

        foreach (var operation in context.Operations)
        {
            if (operation is not DeleteOperation<TDocument> delete)
            {
                continue;
            }

            var field = _renderedField ??= FilterDocuments.RenderField(_field, delete.TypedModel.RenderArgs);
            if (stored is null)
            {
                value = deletedValue(context.Services);
                stored = field.FieldSerializer.ToBsonValue(value);
            }

            if (delete.Document is TSoftDelete document)
            {
                setMember(document, value);
            }

            // A document of its own for each update: combining updates merges later ones into the documents of earlier ones.
            var markDeleted = new BsonDocument("$set", new BsonDocument(field.FieldName, stored));
            context.Replace(delete, delete.ToUpdate(new BsonDocumentUpdateDefinition<TDocument>(markDeleted)));
        }

        return ValueTask.CompletedTask;
    }
}

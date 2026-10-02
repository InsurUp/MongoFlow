using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Gives inserted and replaced documents without a tenant the current one, and rejects those carrying another tenant, as
/// it does updates made with a document, such as a tracked document's changes, and such updates that change the tenant
/// field: a write can't move a document out of the current tenant.
/// </summary>
internal sealed class MultiTenancyInterceptor<TDocument, TTenantEntity, TTenant>(
    Expression<Func<TTenantEntity, TTenant>> tenantId,
    Func<TTenantEntity, TTenant> getTenantId,
    Action<TTenantEntity, TTenant> setTenantId,
    Func<IServiceProvider, TTenant> currentTenantId,
    Func<IServiceProvider, bool>? allTenants,
    Func<TTenant, bool> isUnset) : VaultInterceptor
{
    private readonly Expression<Func<TDocument, TTenant>> _field = MemberExpressions.Rebind<TTenantEntity, TDocument, TTenant>(tenantId);

    // Rendered on first use, with the collection's serializers. Two requests may both render it; they get the same.
    private RenderedFieldDefinition? _renderedField;

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        if (allTenants?.Invoke(context.Services) == true || currentTenantId(context.Services) is not { } current)
        {
            return ValueTask.CompletedTask;
        }

        foreach (var operation in context.Operations)
        {
            if (operation is not (InsertOperation<TDocument> or ReplaceOperation<TDocument> or
                UpdateOperation<TDocument> { Document: not null }))
            {
                continue;
            }

            var document = (TTenantEntity)operation.Document!;
            var tenant = getTenantId(document);
            if (isUnset(tenant))
            {
                // An update writes only what it says, so a tenant set on its document wouldn't be stored.
                if (operation.Kind != OperationKind.Update)
                {
                    setTenantId(document, current);
                }
            }
            else if (!EqualityComparer<TTenant>.Default.Equals(tenant, current))
            {
                throw Rejected(context, operation, tenant);
            }

            if (operation is UpdateOperation<TDocument> update)
            {
                RejectTenantChange(context, update, current);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Rejects an update whose operators change the tenant field, other than to the current tenant: unsetting it, setting
    /// another tenant, or setting a document that holds it. An aggregation pipeline isn't read.
    /// </summary>
    /// <remarks>
    /// The update is rendered to read it, and the rendering takes its place, so the write sends it as it is rather than
    /// rendering it again.
    /// </remarks>
    private void RejectTenantChange(SaveContext context,
        UpdateOperation<TDocument> update,
        TTenant current)
    {
        var args = update.TypedModel.RenderArgs;
        if (update.Update.Render(args) is not BsonDocument operators)
        {
            return;
        }

        update.Update = new BsonDocumentUpdateDefinition<TDocument>(operators);

        var field = _renderedField ??= FilterDocuments.RenderField(_field, args);
        BsonValue? stored = null;

        for (var i = 0; i < operators.ElementCount; i++)
        {
            // Every update operator, such as $set, takes a document of fields.
            var @operator = operators.GetElement(i);
            var fields = @operator.Value.AsBsonDocument;

            for (var j = 0; j < fields.ElementCount; j++)
            {
                var target = fields.GetElement(j);
                if (!ElementPaths.Reaches(target.Name, field.FieldName))
                {
                    continue;
                }

                // Setting the tenant the document is in already leaves it there.
                if (@operator.Name == "$set" && target.Name == field.FieldName &&
                    target.Value.Equals(stored ??= field.FieldSerializer.ToBsonValue(current)))
                {
                    continue;
                }

                throw Rejected(context, update, target.Value);
            }
        }
    }

    /// <summary>The failure of a write of <paramref name="tenant"/>, logged, for the save to throw.</summary>
    private InvalidOperationException Rejected(SaveContext context,
        VaultOperation operation,
        object? tenant)
    {
        var current = currentTenantId(context.Services);
        context.Run.Runtime.Model.Logs.Save.TenantRejected(operation.Namespace.CollectionName, tenant, current);

        return new InvalidOperationException(
            $"{typeof(TDocument).Name} of tenant {tenant} can't be written while the current tenant is {current}.");
    }
}

using MongoDB.Bson;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Interceptors;

public sealed class AuditTrailInterceptor(IAuditVault audit, ICurrentUser user, TimeProvider clock) : VaultInterceptor
{
    public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow().UtcDateTime;

        foreach (var operation in context.Operations)
        {
            var documentType = operation.Collection.DocumentType;

            audit.Entries.Add(new AuditLogEntry
            {
                At = at,
                Collection = operation.Namespace.CollectionName,
                Action = operation.Kind.ToString(),
                UserId = user.UserId,
                Document = operation.Document?.ToBsonDocument(documentType),
                AgencyId = user.AgencyId
            });
        }

        // Joins the transaction this save runs in, so the entries commit or roll back with the change they describe.
        await audit.SaveAsync(cancellationToken);
    }
}

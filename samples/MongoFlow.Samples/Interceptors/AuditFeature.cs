using MongoDB.Bson;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Interceptors;

/// <summary>
/// Records every change in the audit vault. It's a feature, rather than a bare interceptor, so a bulk import can switch
/// it off with <c>Without(AuditFeature.FeatureKey)</c>.
/// </summary>
public sealed class AuditFeature : IVaultFeature
{
    public static readonly FeatureKey FeatureKey = new("audit");

    public FeatureKey Key => FeatureKey;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
        vault.AddInterceptor<AuditTrailInterceptor>(interceptor => interceptor
            .NeedsOriginals()
            .For(collection => collection.DocumentType != typeof(AuditLogEntry)));
}

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
                Before = operation.Original?.ToBsonDocument(documentType),
                After = operation.Document?.ToBsonDocument(documentType),
                AgencyId = user.AgencyId
            });
        }

        // Joins the transaction this save runs in, so the entries commit or roll back with the change they describe.
        await audit.SaveAsync(cancellationToken);
    }
}

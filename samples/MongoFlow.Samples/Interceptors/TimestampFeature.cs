using MongoDB.Driver;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Interceptors;

/// <summary>
/// Stamps <see cref="ITimestamped"/> documents on every write. Registered per collection, so the interceptor knows the
/// document type and can add to update definitions, not just to documents in memory.
/// </summary>
public sealed class TimestampFeature : IVaultFeature, IVaultCollectionConfiguration
{
    public static FeatureKey Key { get; } = new("timestamps");

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault.ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(ITimestamped)))
        {
            collection.AddInterceptor<TimestampInterceptor<TDocument>>();
        }
    }
}

public sealed class TimestampInterceptor<TDocument>(TimeProvider clock) : VaultInterceptor
{
    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var operation in context.Operations.ToList())
        {
            switch (operation)
            {
                case InsertOperation<TDocument> { Document: ITimestamped inserted }:
                    inserted.CreatedAt = now;
                    break;

                case ReplaceOperation<TDocument> { Document: ITimestamped replaced }:
                    replaced.UpdatedAt = now;
                    break;

                // No document to touch, so the stamp goes into the update itself.
                case UpdateOperation<TDocument> update:
                    var stamp = Builders<TDocument>.Update.Set(x => ((ITimestamped)x!).UpdatedAt, now);
                    context.Replace(update, update.WithUpdate(Builders<TDocument>.Update.Combine(update.Update, stamp)));
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }
}

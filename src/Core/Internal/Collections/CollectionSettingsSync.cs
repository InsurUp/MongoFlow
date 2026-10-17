using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Creates a vault's collections with their declared settings, and keeps them as declared: what MongoDB can change in
/// place is changed with <c>collMod</c>, and what it can't fails, before anything is changed.
/// </summary>
internal static class CollectionSettingsSync
{
    /// <summary>The collection as <c>listCollections</c> lists it, or <see langword="null"/> when there's none.</summary>
    public static async Task<BsonDocument?> ListAsync(IMongoDatabase database,
        string name,
        CancellationToken cancellationToken)
    {
        var options = new ListCollectionsOptions { Filter = new BsonDocument("name", name) };
        return await (await database.ListCollectionsAsync(options, cancellationToken)).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Creates <paramref name="collection"/> if it's missing, with its declared settings, or encrypted if it has encrypted
    /// members: before an index is created on it, which would create it plain.
    /// </summary>
    public static async Task EnsureCreatedAsync(VaultModel model,
        ICollectionModel collection,
        CancellationToken cancellationToken)
    {
        if (await ListAsync(model.SourceDatabase, collection.Namespace.CollectionName, cancellationToken) is null)
        {
            await CreateAsync(model, collection, cancellationToken);
        }
    }

    /// <summary>What setting <paramref name="collection"/>'s declared settings up does.</summary>
    public static async Task<CollectionPlan> PlanAsync(VaultModel model,
        ICollectionModel collection,
        CancellationToken cancellationToken)
    {
        var listed = await ListAsync(model.SourceDatabase, collection.Namespace.CollectionName, cancellationToken);
        if (listed is null)
        {
            return new CollectionPlan(collection, SetupAction.Create);
        }

        if (collection.Settings is not { } settings)
        {
            return new CollectionPlan(collection, SetupAction.Unchanged);
        }

        var comparison = CollectionSettingsSpecs.Compare(settings, listed);
        return comparison switch
        {
            { Conflicts.Count: > 0 } => new CollectionPlan(collection, SetupAction.Conflict, Conflicts: comparison.Conflicts),
            { Changes: { } changes } => new CollectionPlan(collection, SetupAction.Update, changes),
            _ => new CollectionPlan(collection, SetupAction.Unchanged)
        };
    }

    /// <summary>
    /// Logs each collection <paramref name="plans"/> can't set up, and fails naming them all, before any is changed.
    /// </summary>
    /// <exception cref="InvalidOperationException">A collection differs in what MongoDB can't change in place.</exception>
    public static void ThrowIfConflicts(VaultModel model,
        IReadOnlyList<CollectionPlan> plans)
    {
        var conflicts = plans.Where(plan => plan.Action == SetupAction.Conflict).ToList();
        if (conflicts.Count == 0)
        {
            return;
        }

        foreach (var plan in conflicts)
        {
            model.Logs.Collections.CollectionDiffers(plan.Collection.Namespace.FullName, string.Join("; ", plan.Conflicts!));
        }

        throw new InvalidOperationException(
            $"{model.VaultType.Name} declares settings MongoDB can't change in place: " +
            string.Join(" ", conflicts.Select(plan => $"{plan.Collection.Namespace.FullName}: {string.Join("; ", plan.Conflicts!)}.")) +
            " Rebuild the collection in a migration: create it anew with context.Collection(...).CreateAsync, and copy the " +
            "documents into it.");
    }

    /// <summary>Does what <paramref name="plan"/> says, in a span of its own.</summary>
    public static async Task ApplyAsync(VaultModel model,
        CollectionPlan plan,
        CancellationToken cancellationToken)
    {
        var collection = plan.Collection;
        var name = collection.Namespace.FullName;
        var log = model.Logs.Collections;

        using var activity = VaultActivities.StartCollection(model.VaultType.Name, collection.Namespace.CollectionName);
        try
        {
            switch (plan.Action)
            {
                case SetupAction.Create:
                    await CreateAsync(model, collection, cancellationToken);
                    break;

                case SetupAction.Update:
                    var command = new BsonDocument("collMod", collection.Namespace.CollectionName).AddRange(plan.Changes!);
                    await model.SourceDatabase.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
                    log.CollectionUpdated(name, plan.Changes!.ToJson());
                    break;

                case SetupAction.Unchanged when collection.Settings is not null:
                    log.CollectionUpToDate(name);
                    break;
            }

            activity.Changed(plan.Action);
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Creates <paramref name="collection"/>: encrypted, through the vault's encryption, if it has encrypted members, or
    /// with its declared settings.
    /// </summary>
    private static async Task CreateAsync(VaultModel model,
        ICollectionModel collection,
        CancellationToken cancellationToken)
    {
        if (collection.Encryption is not null)
        {
            await model.Encryption!.PrepareCollectionAsync(model, collection, cancellationToken);
            return;
        }

        await model.SourceDatabase.CreateCollectionAsync(collection.Namespace.CollectionName,
            collection.Settings?.CreateOptions(),
            cancellationToken);

        if (collection.Settings is { } settings)
        {
            model.Logs.Collections.CollectionCreated(collection.Namespace.FullName, settings.Definition);
        }
    }
}

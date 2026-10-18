using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// Sets up the indexes a vault's collections declare, after its migrations, which may remove what a unique index would
/// refuse, or rename the fields an index is on: creates those missing, changes in place the expiry and visibility of
/// those defined otherwise, and fails, before changing any, when one differs in what MongoDB can't change in place.
/// </summary>
internal sealed class IndexSetup : IVaultSetup
{
    public bool BeforeMigrations => false;

    public async Task RunAsync(VaultModel model,
        MigrateOptions options,
        CancellationToken cancellationToken)
    {
        // Synced instead, with every vault's declarations, once every vault's migrations have run.
        if (options.SyncIndexes)
        {
            return;
        }

        var planned = new List<(ICollectionModel Collection, bool Exists, IReadOnlyList<IndexPlan> Plans)>();
        foreach (var collection in model.Collections.Where(collection => collection.Indexes.Count > 0))
        {
            var (exists, plans) = await PlanAsync(model, collection, collection.Indexes, cancellationToken);
            planned.Add((collection, exists, plans));
        }

        ThrowIfConflicts(model, planned.SelectMany(collection => collection.Plans.Select(plan => (collection.Collection, plan))));

        foreach (var (collection, exists, plans) in planned)
        {
            await ApplyAsync(model, collection, exists, plans, cancellationToken);
        }
    }

    /// <summary>Whether <paramref name="collection"/> exists, and what setting up <paramref name="indexes"/> on it does.</summary>
    public static async Task<(bool Exists, IReadOnlyList<IndexPlan> Plans)> PlanAsync(VaultModel model,
        ICollectionModel collection,
        IEnumerable<IndexDeclaration> indexes,
        CancellationToken cancellationToken)
    {
        var (exists, listed, collation) = await IndexSync.ListAsync(model.SourceDatabase, collection.Namespace.CollectionName,
            cancellationToken);

        // A collection that doesn't exist yet is created with its declared collation, which its indexes then have.
        if (!exists)
        {
            collation = StoredSpecs.Collation(collection.Settings?.Collation);
        }

        return (exists, [.. indexes.Select(index => IndexSpecs.Plan(index, listed, collation))]);
    }

    /// <summary>Logs each index <paramref name="plans"/> can't set up, and fails naming them all, before any is changed.</summary>
    /// <exception cref="InvalidOperationException">An index differs in what MongoDB can't change in place.</exception>
    public static void ThrowIfConflicts(VaultModel model,
        IEnumerable<(ICollectionModel Collection, IndexPlan Plan)> plans)
    {
        var conflicts = plans.Where(planned => planned.Plan.Action == SetupAction.Conflict).ToList();
        if (conflicts.Count == 0)
        {
            return;
        }

        foreach (var (collection, plan) in conflicts)
        {
            model.Logs.Indexes.IndexDiffers(plan.Declaration.Name, collection.Namespace.FullName, string.Join("; ", plan.Conflicts!));
        }

        throw new InvalidOperationException(
            $"{model.VaultType.Name} declares indexes MongoDB can't change in place: " +
            string.Join(" ", conflicts.Select(planned =>
                $"the index {planned.Plan.Declaration.Name} of {planned.Collection.Namespace.FullName}: " +
                $"{string.Join("; ", planned.Plan.Conflicts!)}.")) +
            " Recreate them in a migration, with context.Collection(...).RecreateIndexAsync(name), which runs before the " +
            "migrator sets up indexes, or migrate with MigrateOptions.SyncIndexes, which recreates them.");
    }

    /// <summary>
    /// Does what <paramref name="plans"/> say, each index in a span of its own, creating the collection with its declared
    /// settings first if it doesn't exist.
    /// </summary>
    public static async Task ApplyAsync(VaultModel model,
        ICollectionModel collection,
        bool exists,
        IReadOnlyList<IndexPlan> plans,
        CancellationToken cancellationToken)
    {
        if (!exists && plans.Any(plan => plan.Action == SetupAction.Create))
        {
            await CollectionSettingsSync.EnsureCreatedAsync(model, collection, cancellationToken);
        }

        foreach (var plan in plans)
        {
            await ApplyAsync(model, collection, plan, cancellationToken);
        }
    }

    /// <summary>Does what <paramref name="plan"/> says to one index, in a span of its own.</summary>
    public static async Task ApplyAsync(VaultModel model,
        ICollectionModel collection,
        IndexPlan plan,
        CancellationToken cancellationToken)
    {
        var index = plan.Declaration;
        var name = collection.Namespace.FullName;
        var log = model.Logs.Indexes;
        var database = model.SourceDatabase;
        var collectionName = collection.Namespace.CollectionName;

        using var activity = VaultActivities.StartIndex(model.VaultType.Name, collectionName, index.Name);
        try
        {
            if (plan is { Action: SetupAction.Recreate, Replaces: { } replaced })
            {
                // Dropped first, as MongoDB keeps one index of a name, and one of the same keys and options; logged once
                // it's gone, so it can be created again by hand should creating the declared one fail.
                var dropped = IndexSpecs.NameOf(replaced)!;
                await IndexSync.DropAsync(database, collectionName, dropped, cancellationToken);
                log.IndexDropped(dropped,
                    name,
                    dropped == index.Name
                        ? $"to recreate it as declared: {string.Join("; ", plan.Conflicts!)}"
                        : $"as the declared {index.Name} replaces it, on the same keys with the same options",
                    IndexSpecs.DefinitionOf(replaced));

                await IndexSync.ApplyAsync(database, collectionName, plan with { Action = SetupAction.Create }, cancellationToken);
                log.IndexRecreated(index.Name, name, dropped, index.Definition);
            }
            else
            {
                await IndexSync.ApplyAsync(database, collectionName, plan, cancellationToken);
            }

            activity.Changed(plan.Action);

            switch (plan.Action)
            {
                case SetupAction.Create:
                    log.IndexCreatedAsDeclared(index.Name, name, index.Definition);
                    break;

                case SetupAction.Update:
                    log.IndexChangedInPlace(index.Name, name, plan.Changes!.ToJson());
                    break;

                case SetupAction.MakeUnique:
                    log.IndexChangedInPlace(index.Name, name,
                        new BsonDocument(plan.Changes ?? new BsonDocument("name", index.Name)).Add("unique", true).ToJson());
                    break;

                case SetupAction.Recreate:
                    break;

                default:
                    log.IndexUpToDate(index.Name, name);
                    break;
            }
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }

    /// <summary>Drops <paramref name="listed"/>, an index no vault declares, in a span of its own, logging its definition.</summary>
    public static async Task DropAsync(VaultModel model,
        ICollectionModel collection,
        BsonDocument listed,
        CancellationToken cancellationToken)
    {
        var index = IndexSpecs.NameOf(listed)!;

        using var activity = VaultActivities.StartIndex(model.VaultType.Name, collection.Namespace.CollectionName, index);
        try
        {
            await IndexSync.DropAsync(model.SourceDatabase, collection.Namespace.CollectionName, index, cancellationToken);
            activity.Changed(SetupAction.Drop);
            model.Logs.Indexes.IndexDropped(index, collection.Namespace.FullName, "which no vault declares", IndexSpecs.DefinitionOf(listed));
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }
}

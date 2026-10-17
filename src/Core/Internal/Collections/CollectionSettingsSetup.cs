namespace MongoFlow;

/// <summary>
/// Sets up the settings a vault's collections declare, before its migrations, which would otherwise create a collection
/// without them by writing to it: creates the collections missing with them, changes in place the validator, change
/// stream images and expiry of those that differ, and fails, before changing any, when one differs in what MongoDB can't
/// change in place. Encrypted collections are set up by the vault's encryption first.
/// </summary>
internal sealed class CollectionSettingsSetup : IVaultSetup
{
    public bool BeforeMigrations => true;

    public async Task RunAsync(VaultModel model,
        MigrateOptions options,
        CancellationToken cancellationToken)
    {
        var plans = new List<CollectionPlan>();
        foreach (var collection in model.Collections.Where(collection => collection.Settings is not null))
        {
            plans.Add(await CollectionSettingsSync.PlanAsync(model, collection, cancellationToken));
        }

        CollectionSettingsSync.ThrowIfConflicts(model, plans);

        foreach (var plan in plans)
        {
            await CollectionSettingsSync.ApplyAsync(model, plan, cancellationToken);
        }
    }
}

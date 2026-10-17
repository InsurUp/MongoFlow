using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A vault collection's declared settings and indexes, for a migration to apply where it needs them, from
/// <see cref="MigrationContext{TVault}.Collection{TDocument}"/>.
/// </summary>
/// <remarks>
/// The server doesn't build indexes or change a collection in a transaction, so each method runs outside the migration's,
/// which it needs the migration to opt out of, with <c>UseTransaction =&gt; false</c>: what it did stays if the migration
/// fails afterwards. A name the collection doesn't declare, such as one renamed since the migration was written, is
/// logged and left to the declarations the migrator applies after the migrations.
/// </remarks>
public sealed class DeclaredCollection<TDocument>
{
    private readonly VaultModel _model;
    private readonly ICollectionModel _collection;
    private readonly IClientSessionHandle _session;

    internal DeclaredCollection(VaultModel model,
        ICollectionModel collection,
        IClientSessionHandle session)
    {
        _model = model;
        _collection = collection;
        _session = session;
    }

    /// <summary>
    /// Creates the collection with its declared settings, encrypted if it has encrypted members, as a migration that
    /// replaces it creates it anew. One that exists is set up as the migrator sets it up: what differs is changed in place
    /// where MongoDB can, and fails where it can't.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The migration runs in a transaction, or the collection differs from its settings in what MongoDB can't change in
    /// place.
    /// </exception>
    public async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfInTransaction();

        if (_collection.Encryption is not null)
        {
            await _model.Encryption!.PrepareCollectionAsync(_model, _collection, cancellationToken);
        }

        var plan = await CollectionSettingsSync.PlanAsync(_model, _collection, cancellationToken);
        CollectionSettingsSync.ThrowIfConflicts(_model, [plan]);
        await CollectionSettingsSync.ApplyAsync(_model, plan, cancellationToken);
    }

    /// <summary>
    /// Sets up every index the collection declares as the migrator does after the migrations: creates those missing, and
    /// changes in place the expiry and visibility of those defined otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The migration runs in a transaction, or an index differs from its declaration in what MongoDB can't change in place:
    /// see <see cref="RecreateIndexAsync"/>.
    /// </exception>
    public Task ApplyIndexesAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(_collection.Indexes, cancellationToken);

    /// <summary>Sets up the declared indexes named <paramref name="names"/>, as the migrator does after the migrations.</summary>
    /// <inheritdoc cref="ApplyIndexesAsync(CancellationToken)" path="/exception"/>
    public Task ApplyIndexesAsync(IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        var indexes = new List<IndexDeclaration>();
        foreach (var name in names)
        {
            if (Declared(name) is { } index)
            {
                indexes.Add(index);
            }
            else
            {
                _model.Logs.Indexes.IndexNotDeclared(name, _collection.Namespace.FullName);
            }
        }

        return ApplyAsync(indexes, cancellationToken);
    }

    /// <summary>
    /// Drops the index named <paramref name="name"/>, and any the server would refuse the declared one beside, on the same
    /// keys with the same options, then creates it as declared: for a change MongoDB can't make in place, such as making
    /// it unique once no two documents share its keys. Queries go without it in between.
    /// </summary>
    /// <exception cref="InvalidOperationException">The migration runs in a transaction.</exception>
    public async Task RecreateIndexAsync(string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ThrowIfInTransaction();

        var collection = _collection.Namespace;
        if (Declared(name) is not { } index)
        {
            _model.Logs.Indexes.IndexNotDeclared(name, collection.FullName);
            await IndexSync.RecreateAsync(_model.SourceDatabase, collection.CollectionName, name, declared: null, cancellationToken);
            return;
        }

        await CollectionSettingsSync.EnsureCreatedAsync(_model, _collection, cancellationToken);

        using var activity = VaultActivities.StartIndex(_model.VaultType.Name, collection.CollectionName, name);
        try
        {
            var dropped = await IndexSync.RecreateAsync(_model.SourceDatabase, collection.CollectionName, name, index, cancellationToken);
            activity.Recreated();
            _model.Logs.Indexes.IndexRecreated(name,
                collection.FullName,
                dropped.Count > 0 ? string.Join(", ", dropped) : "none",
                index.Definition);
        }
        catch (Exception exception)
        {
            activity.Fail(exception);
            throw;
        }
    }

    private async Task ApplyAsync(IReadOnlyList<IndexDeclaration> indexes,
        CancellationToken cancellationToken)
    {
        ThrowIfInTransaction();

        var (exists, plans) = await IndexSetup.PlanAsync(_model, _collection, indexes, cancellationToken);
        IndexSetup.ThrowIfConflicts(_model, plans.Select(plan => (_collection, plan)));
        await IndexSetup.ApplyAsync(_model, _collection, exists, plans, cancellationToken);
    }

    private IndexDeclaration? Declared(string name) =>
        _collection.Indexes.FirstOrDefault(index => string.Equals(index.Name, name, StringComparison.Ordinal));

    private void ThrowIfInTransaction()
    {
        if (_session.IsInTransaction)
        {
            throw new InvalidOperationException(
                $"Setting up {_collection.Namespace.FullName} runs outside the migration's transaction, as the server " +
                "doesn't build indexes or change a collection in one, and waiting on what the transaction wrote would abort " +
                "it. Opt the migration out with UseTransaction => false.");
        }
    }
}

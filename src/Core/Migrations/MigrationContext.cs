using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>What a migration works with.</summary>
public sealed class MigrationContext<TVault> where TVault : MongoVault
{
    private readonly VaultModel _model;

    internal MigrationContext(TVault vault,
        VaultModel model,
        IClientSessionHandle session)
    {
        Vault = vault;
        _model = model;
        Database = model.Database;
        Session = session;
    }

    /// <summary>
    /// The vault, from the migration's own DI scope. Its saves join the migration's transaction. Query filters and
    /// features still apply, so switch off what the migration shouldn't be limited by, such as multi-tenancy.
    /// </summary>
    public TVault Vault { get; }

    /// <summary>
    /// The vault's database, for work through the driver, such as on indexes the vault doesn't declare. Query filters and
    /// features don't apply.
    /// </summary>
    public IMongoDatabase Database { get; }

    /// <summary>The session to pass to driver calls. It's in a transaction unless the migration opted out.</summary>
    public IClientSessionHandle Session { get; }

    /// <summary>
    /// The settings and indexes <paramref name="collection"/>, such as <c>x =&gt; x.Orders</c>, declares, for the migration
    /// to apply where it needs them: an index the migrations after it rely on, or a collection it creates anew. The
    /// migrator applies them anyway: settings before the vault's migrations, indexes after.
    /// </summary>
    public DeclaredCollection<TDocument> Collection<TDocument>(Expression<Func<TVault, IVaultCollection<TDocument>>> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        return new DeclaredCollection<TDocument>(_model, _model.CollectionsByDocument[typeof(TDocument)], Session);
    }
}

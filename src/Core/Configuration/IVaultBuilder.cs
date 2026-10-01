using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Configures <typeparamref name="TVault"/>. Passed to the registration delegate, to
/// <see cref="IConfigurableVault{TSelf}.Configure"/>, to <see cref="IVaultConfiguration{TVault}"/> and to
/// <see cref="IVaultFeature"/>.
/// </summary>
/// <remarks>
/// Sources apply in this order: default configurations, the vault's own
/// <see cref="IConfigurableVault{TSelf}.Configure"/>, then the registration delegate. For a setting that holds one value
/// the last one wins; filters and features accumulate in that order. Everything is applied once, at startup, and the
/// result is validated before first use.
/// </remarks>
public interface IVaultBuilder<TVault> where TVault : MongoVault
{
    /// <summary>
    /// The collections declared on the vault as <c>IVaultCollection&lt;TDocument&gt;</c> or
    /// <c>IVaultCollection&lt;TDocument, TKey&gt;</c> properties.
    /// </summary>
    IReadOnlyList<IVaultCollectionInfo> Collections { get; }

    /// <summary>Uses the named database on the <see cref="IMongoClient"/> registered in DI.</summary>
    IVaultBuilder<TVault> UseDatabase(string name);

    IVaultBuilder<TVault> UseDatabase(IMongoDatabase database);

    /// <summary>Applies a configuration created from the root provider. Each configuration type applies once per vault.</summary>
    IVaultBuilder<TVault> UseConfiguration<TConfiguration>() where TConfiguration : class, IVaultConfiguration<TVault>;

    IVaultBuilder<TVault> UseConfiguration(IVaultConfiguration<TVault> configuration);

    /// <summary>
    /// Stops a configuration registered with <c>AddDefaultVaultConfiguration</c> from applying to this vault, such as
    /// <c>SkipDefaultConfiguration&lt;AuditConfiguration&lt;PolicyDatabase&gt;&gt;()</c>. Calling it from a default
    /// configuration fails at startup.
    /// </summary>
    IVaultBuilder<TVault> SkipDefaultConfiguration<TConfiguration>() where TConfiguration : class, IVaultConfiguration<TVault>;

    /// <summary>
    /// Stops every configuration registered with <c>AddDefaultVaultConfiguration</c> from applying to this vault.
    /// Configurations applied with <see cref="UseConfiguration{TConfiguration}"/> still apply, so a vault can skip all
    /// defaults and opt back in to some. Calling it from a default configuration fails at startup.
    /// </summary>
    IVaultBuilder<TVault> SkipAllDefaultConfigurations();

    /// <summary>Configures a keyless collection, selected by its property, such as <c>x =&gt; x.Logs</c>.</summary>
    /// <remarks>
    /// A default configuration can select collections through its constraints: with
    /// <c>where TVault : IAuditedVault</c>, a property <c>IAuditedVault</c> declares resolves to the vault's own.
    /// </remarks>
    IVaultBuilder<TVault> Collection<TDocument>(
        Expression<Func<TVault, IVaultCollection<TDocument>>> collection,
        Action<IVaultCollectionBuilder<TDocument>> configure);

    /// <summary>
    /// Configures a keyed collection, selected by its property, such as <c>x =&gt; x.Policies</c>. Its builder can set
    /// the key, checked against the <typeparamref name="TKey"/> the property declares.
    /// </summary>
    IVaultBuilder<TVault> Collection<TDocument, TKey>(
        Expression<Func<TVault, IVaultCollection<TDocument, TKey>>> collection,
        Action<IVaultCollectionBuilder<TDocument, TKey>> configure);

    /// <summary>Calls <paramref name="configuration"/> once for each collection declared on the vault.</summary>
    IVaultBuilder<TVault> ForEachCollection(IVaultCollectionConfiguration configuration);

    /// <summary>
    /// Adds a query filter to every collection whose document is assignable to <typeparamref name="TTarget"/>, such as an
    /// interface the documents implement. Collections that don't match are left alone, so a default configuration can
    /// add filters only some vaults use.
    /// </summary>
    IVaultBuilder<TVault> QueryFilter<TTarget>(Expression<Func<TTarget, bool>> filter);

    /// <summary>
    /// Adds a query filter, decided per query from the request's services, to every collection whose document is
    /// assignable to <typeparamref name="TTarget"/>. Return <c>_ =&gt; true</c> to leave the query unfiltered or
    /// <c>_ =&gt; false</c> to match nothing.
    /// </summary>
    IVaultBuilder<TVault> QueryFilter<TTarget>(Func<IServiceProvider, Expression<Func<TTarget, bool>>> filter);

    /// <inheritdoc cref="QueryFilter{TTarget}(Func{IServiceProvider, Expression{Func{TTarget, bool}}})"/>
    /// <remarks>Resolved when the query executes.</remarks>
    IVaultBuilder<TVault> QueryFilter<TTarget>(
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TTarget, bool>>>> filter);

    /// <summary>
    /// Adds an interceptor that sees operations on every collection. It's created from the request's services once per
    /// vault instance, so it can depend on scoped services, and disposed with the request's scope.
    /// </summary>
    IVaultBuilder<TVault> AddInterceptor<TInterceptor>(Action<IInterceptorBuilder>? configure = null)
        where TInterceptor : VaultInterceptor;

    /// <summary>
    /// Adds an interceptor instance, shared by every request, that sees operations on every collection. MongoFlow doesn't
    /// dispose it.
    /// </summary>
    IVaultBuilder<TVault> AddInterceptor(VaultInterceptor interceptor, Action<IInterceptorBuilder>? configure = null);

    /// <summary>Adds a feature created from the root provider.</summary>
    IVaultBuilder<TVault> AddFeature<TFeature>() where TFeature : class, IVaultFeature;

    IVaultBuilder<TVault> AddFeature<TFeature>(TFeature feature) where TFeature : class, IVaultFeature;

    /// <summary>Declares the vault's migrations, which <see cref="IVaultMigrator"/> applies.</summary>
    IVaultBuilder<TVault> Migrations(Action<IMigrationBuilder<TVault>> configure);
}

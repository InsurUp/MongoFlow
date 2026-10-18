using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// Declares what a collection is created with, which <c>IVaultMigrator.MigrateAllAsync</c> creates it with before the
/// vault's migrations, so they write to it as declared. A setting not declared is left as the server has it.
/// </summary>
/// <remarks>
/// A collection that exists has its validator, change stream images and expiry changed in place when they differ. MongoDB
/// can't change a collection's collation or clustering, so the migrator fails, naming them, for a migration to rebuild it.
/// Capped and time series collections can't be declared: the server refuses writes to them in a transaction, which every
/// save is in.
/// </remarks>
public interface ICollectionSettingsBuilder<TDocument>
{
    /// <summary>
    /// How the collection's queries and indexes compare strings, unless they say otherwise, key lookups included, such as
    /// <c>new Collation("tr", strength: CollationStrength.Secondary)</c> to ignore case. MongoDB sets it when the collection
    /// is created, and can't change it.
    /// </summary>
    ICollectionSettingsBuilder<TDocument> Collation(Collation collation);

    /// <summary>
    /// The rule every document written must match, such as <c>x =&gt; x.Total &gt;= 0</c>; <paramref name="level"/> is
    /// which writes are checked, and <paramref name="action"/> what happens to a document that fails. Changed in place.
    /// </summary>
    ICollectionSettingsBuilder<TDocument> Validator(Expression<Func<TDocument, bool>> rule,
        DocumentValidationLevel level = DocumentValidationLevel.Strict,
        DocumentValidationAction action = DocumentValidationAction.Error);

    /// <summary>
    /// The rule every document written must match, built with the driver, such as
    /// <c>Builders&lt;T&gt;.Filter.JsonSchema(schema)</c>. Changed in place.
    /// </summary>
    /// <inheritdoc cref="Validator(Expression{Func{TDocument, bool}}, DocumentValidationLevel, DocumentValidationAction)"/>
    ICollectionSettingsBuilder<TDocument> Validator(FilterDefinition<TDocument> rule,
        DocumentValidationLevel level = DocumentValidationLevel.Strict,
        DocumentValidationAction action = DocumentValidationAction.Error);

    /// <summary>Whether change streams can show documents as they were before a change, and after. Changed in place.</summary>
    ICollectionSettingsBuilder<TDocument> ChangeStreamPreAndPostImages(bool enabled = true);

    /// <summary>
    /// Stores the documents ordered by <c>_id</c>, in the collection itself rather than beside an index on it; with
    /// <paramref name="expireAfter"/>, documents are deleted once their <c>_id</c> is older than it, as MongoDB expires a
    /// clustered collection's documents. MongoDB clusters a collection only when it's created; the expiry is changed in
    /// place.
    /// </summary>
    ICollectionSettingsBuilder<TDocument> Clustered(TimeSpan? expireAfter = null);

    /// <summary>
    /// Has the migrator create the collection when it's missing, as declaring any other setting does, so no save creates
    /// it: a save that creates a collection in its transaction while another does too fails. A collection that exists is
    /// left as it is.
    /// </summary>
    ICollectionSettingsBuilder<TDocument> CreateCollection();
}

using System.Linq.Expressions;
using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>
/// MongoDB Queryable Encryption for a vault: members declared encrypted are stored encrypted, by MongoFlow itself, so it
/// works on MongoDB Community, and the values a vault's filters compare them with are encrypted too. The migrator creates
/// the encrypted collections and their keys, and rebuilds collections that hold data already.
/// </summary>
public static class QueryableEncryptionFeature
{
    /// <summary>
    /// The key of the Queryable Encryption feature, added with <c>UseQueryableEncryption</c>. Switching it off on a view
    /// changes nothing: a vault reads and writes encrypted members through its client that encrypts, whatever the view.
    /// </summary>
    public static FeatureKey Key { get; } = new("queryable-encryption");

    /// <summary>
    /// Makes the vault encrypt the members its collections declare with <c>Encrypt</c>. The vault reads and writes through
    /// a client that encrypts, made from the one it was given, which vaults with the same settings share.
    /// </summary>
    /// <remarks>
    /// Run <c>IVaultMigrator.MigrateAllAsync</c> before the vault is used: it creates the encrypted collections and their
    /// keys, and rebuilds as encrypted a collection that holds data already.
    /// </remarks>
    /// <param name="vault">The vault to encrypt.</param>
    /// <param name="configure">Where the data keys are kept, and the KMS that protects them.</param>
    public static IVaultBuilder<TVault> UseQueryableEncryption<TVault>(this IVaultBuilder<TVault> vault,
        Action<QueryableEncryptionOptions> configure)
        where TVault : MongoVault
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new QueryableEncryptionOptions();
        configure(options);

        return vault.AddFeature(new EncryptionFeature(options));
    }

    /// <summary>
    /// Stores <paramref name="member"/> encrypted, queryable as <paramref name="query"/> allows, or not at all without
    /// one. Its BSON type comes from its serializer, unless <paramref name="bsonType"/> says it.
    /// </summary>
    /// <param name="collection">The collection whose documents hold the member.</param>
    /// <param name="member">The member, such as <c>c =&gt; c.Ssn</c> or <c>c =&gt; c.Address.Street</c>.</param>
    /// <param name="query">The queries it allows, such as <see cref="EncryptedQuery.Equality"/>.</param>
    /// <param name="bsonType">Its BSON type, where its serializer doesn't tell.</param>
    public static IVaultCollectionBuilder<TDocument> Encrypt<TDocument, TMember>(this IVaultCollectionBuilder<TDocument> collection,
        Expression<Func<TDocument, TMember>> member,
        EncryptedQuery? query = null,
        BsonType? bsonType = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(member);

        ((CollectionModelBuilder<TDocument>)collection).Encrypt(member, new EncryptedField(query, bsonType));
        return collection;
    }

    /// <inheritdoc cref="Encrypt{TDocument, TMember}(IVaultCollectionBuilder{TDocument}, Expression{Func{TDocument, TMember}}, EncryptedQuery?, BsonType?)"/>
    public static IVaultCollectionBuilder<TDocument, TKey> Encrypt<TDocument, TKey, TMember>(
        this IVaultCollectionBuilder<TDocument, TKey> collection,
        Expression<Func<TDocument, TMember>> member,
        EncryptedQuery? query = null,
        BsonType? bsonType = null)
    {
        Encrypt((IVaultCollectionBuilder<TDocument>)collection, member, query, bsonType);
        return collection;
    }
}

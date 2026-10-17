using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using TUnit.Assertions.Enums;

namespace MongoFlow.Tests.Indexes;

/// <summary>
/// A collection's <c>Index</c> and <c>Settings</c> declare what the migrator sets up:
/// <list type="number">
/// <item>keys, partial filters and validators are rendered with the collection's serializers, and an index without a name
/// is named as the server names one;</item>
/// <item>a vault's own index takes the place of a default's of its name, its own setting wins over a default's, settings
/// declared again are configured further, and the indexes of a feature the collection is without are left out;</item>
/// <item>an index the server would refuse, one declared twice, two the server keeps one of, and an index, partial filter
/// or validator on an encrypted member fail the model;</item>
/// <item>a vault declaring settings or indexes gets the migrator's setups for them, in order.</item>
/// </list>
/// </summary>
public partial class IndexBuilderTests
{
    [Test]
    public async Task Index_Declared_IsRenderedWithTheCollectionsSerializers()
    {
        // Arrange
        await using var host = Host(invoices => invoices
            .Index(keys => keys.Ascending(x => x.Number), index => index.Unique())
            .Index(keys => keys.Ascending(x => x.Customer).Descending(x => x.IssuedAt))
            .Index(keys => keys.Ascending(x => x.DueAt), index => index
                .Name("due")
                .Partial(x => x.Status == InvoiceStatus.Issued)
                .ExpireAfter(TimeSpan.FromDays(30)))
            .Index(keys => keys.Text(x => x.Notes).Ascending(x => x.Address!.City), index => index.Sparse().Hidden())
            .Index(keys => keys.Hashed(x => x.Customer))
            .Index(keys => keys.Wildcard(x => x.Address), index => index.Collation(new Collation("tr", strength: CollationStrength.Secondary)))
            .Index(keys => keys.Ascending(x => x.Total), index => index.Partial(Builders<Invoice>.Filter.Exists(x => x.Notes))));

        // Act
        var indexes = Indexes(host);

        // Assert
        await Verify(indexes);
    }

    [Test]
    public async Task Settings_Declared_AreRenderedWithTheCollectionsSerializers()
    {
        // Arrange
        await using var host = Host(invoices => invoices
            .Settings(settings => settings
                .Collation(new Collation("tr", strength: CollationStrength.Secondary))
                .Validator(x => x.Total >= 0 && x.Status != InvoiceStatus.Draft, DocumentValidationLevel.Moderate, DocumentValidationAction.Warn)
                .ChangeStreamPreAndPostImages()
                .Clustered(TimeSpan.FromDays(365))));

        // Act
        var settings = Collection(host).Settings;

        // Assert
        await Verify(settings);
    }

    [Test]
    public async Task IndexAndSettings_InADefaultAndTheVaultsOwn_TheOwnWin()
    {
        // Arrange — the default names its index by_customer and compares in English.
        await using var host = new VaultHost<BillingVault>(
            vault => vault.Collection(x => x.Invoices, invoices => invoices
                .Index(keys => keys.Ascending(x => x.Customer).Ascending(x => x.IssuedAt), index => index.Name("by_customer"))
                .Settings(settings => settings.Collation(new Collation("tr")))),
            services => services.AddDefaultVaultConfiguration(typeof(InvoiceDefaults<>)));

        // Act
        var collection = Collection(host);

        // Assert
        await Verify(new { Indexes = Indexes(host), collection.Settings });
    }

    [Test]
    public async Task Settings_DeclaredAgain_AreConfiguredFurther()
    {
        // Arrange
        await using var host = Host(invoices => invoices
            .Settings(settings => settings.Collation(new Collation("tr")).ChangeStreamPreAndPostImages())
            .Settings(settings => settings.ChangeStreamPreAndPostImages(false).Clustered()));

        // Act
        var settings = Collection(host).Settings;

        // Assert
        await Verify(settings);
    }

    [Test]
    public async Task Index_OfAFeatureTheCollectionIsWithout_IsLeftOut()
    {
        // Arrange
        await using var host = new VaultHost<BillingVault>(vault => vault
            .AddFeature(new TenantIndexFeature())
            .Collection(x => x.Invoices, invoices => invoices.Without(TenantIndexFeature.Key)));

        // Act
        var indexes = Collection(host).Indexes;

        // Assert
        await Assert.That(indexes).IsEmpty();
    }

    [Test]
    public async Task Index_OfAFeature_IsDeclared()
    {
        // Arrange
        await using var host = new VaultHost<BillingVault>(vault => vault.AddFeature(new TenantIndexFeature()));

        // Act
        var names = Collection(host).Indexes.Select(index => index.Name);

        // Assert
        await Assert.That(names).IsEquivalentTo(["TenantId_1"]);
    }

    [Test]
    [MethodDataSource(nameof(Refused))]
    public async Task Index_TheServerWouldRefuse_ThrowsVaultConfigurationException(string name,
        Action<IVaultCollectionBuilder<Invoice, int>> invoices)
    {
        // Arrange
        await using var host = Host(invoices);

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace().UseParameters(name);
    }

    [Test]
    [MethodDataSource(nameof(OnEncrypted))]
    public async Task IndexAndSettings_OnAnEncryptedMember_ThrowVaultConfigurationException(string name,
        Action<IVaultCollectionBuilder<Invoice, int>> invoices)
    {
        // Arrange
        await using var host = EncryptedHost(invoices);

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace().UseParameters(name);
    }

    [Test]
    public async Task Index_OnWhatsNotEncrypted_IsDeclared()
    {
        // Arrange
        await using var host = EncryptedHost(invoices => invoices
            .Index(keys => keys.Ascending(x => x.Customer), index => index.Partial(x => x.Status == InvoiceStatus.Issued))
            .Settings(settings => settings.Validator(x => x.Total >= 0)));

        // Act
        var collection = Collection(host);

        // Assert
        await Verify(new { Indexes = collection.Indexes.Select(index => index.Name), collection.Settings!.Validator });
    }

    [Test]
    [MethodDataSource(nameof(Arguments))]
    public async Task IndexAndSettings_InvalidArgument_ThrowArgumentException(string _,
        Action<IVaultCollectionBuilder<Invoice, int>> invoices,
        Type expected)
    {
        // Arrange
        await using var host = Host(invoices);

        // Act
        var exception = await Assert.That(() => host.Vault).Throws<ArgumentException>();

        // Assert
        await Assert.That(exception!.GetType()).IsEqualTo(expected);
    }

    [Test]
    public async Task Build_NothingDeclared_HasNoSetups()
    {
        // Arrange
        await using var host = new VaultHost<BillingVault>();

        // Act
        var setups = Model(host).Setups;

        // Assert
        await Assert.That(setups).IsEmpty();
    }

    [Test]
    public async Task Build_SettingsIndexesAndSearchIndexesDeclared_HasTheMigratorSetThemUpInOrder()
    {
        // Arrange
        await using var host = Host(invoices => invoices
            .Index(keys => keys.Ascending(x => x.Customer))
            .Settings(settings => settings.Collation(new Collation("tr")))
            .SearchIndex("default", index => index.Dynamic()));

        // Act
        var setups = Model(host).Setups.Select(setup => setup.GetType().Name);

        // Assert
        await Assert.That(setups).IsEquivalentTo(["CollectionSettingsSetup", "IndexSetup", "SearchIndexSetup"], CollectionOrdering.Matching);
    }

    public static IEnumerable<Func<(string, Action<IVaultCollectionBuilder<Invoice, int>>)>> Refused()
    {
        yield return () => ("no keys", invoices => invoices.Index(keys => keys.Combine()));
        yield return () => ("_id alone", invoices => invoices.Index(keys => keys.Ascending(x => x.Id)));
        yield return () => ("expiring by two fields", invoices => invoices.Index(
            keys => keys.Ascending(x => x.DueAt).Ascending(x => x.IssuedAt), index => index.ExpireAfter(TimeSpan.Zero)));
        yield return () => ("declared twice", invoices => invoices
            .Index(keys => keys.Ascending(x => x.Customer))
            .Index(keys => keys.Ascending(x => x.Customer), index => index.Unique()));
        yield return () => ("one index the server keeps", invoices => invoices
            .Index(keys => keys.Ascending(x => x.Customer))
            .Index(keys => keys.Ascending(x => x.Customer), index => index.Name("by_customer").Hidden()));
    }

    public static IEnumerable<Func<(string, Action<IVaultCollectionBuilder<Invoice, int>>)>> OnEncrypted()
    {
        yield return () => ("key", invoices => invoices.Index(keys => keys.Ascending(x => x.TaxNumber)));
        yield return () => ("wildcard", invoices => invoices.Index(keys => keys.Wildcard()));
        yield return () => ("partial filter", invoices => invoices.Index(
            keys => keys.Ascending(x => x.Customer), index => index.Partial(x => x.TaxNumber != "")));
        yield return () => ("validator", invoices => invoices.Settings(settings => settings.Validator(x => x.TaxNumber != "")));
        yield return () => ("JSON schema", invoices => invoices.Settings(settings => settings.Validator(
            Builders<Invoice>.Filter.JsonSchema(BsonDocument.Parse("{ properties: { TaxNumber: { bsonType: 'string' } } }")))));
    }

    public static IEnumerable<Func<(string, Action<IVaultCollectionBuilder<Invoice, int>>, Type)>> Arguments()
    {
        yield return () => ("blank name", invoices => invoices.Index(keys => keys.Ascending(x => x.Customer), index => index.Name(" ")),
            typeof(ArgumentException));
        yield return () => ("no keys builder result", invoices => invoices.Index(_ => null!), typeof(ArgumentException));
        yield return () => ("negative expiry", invoices => invoices.Index(
            keys => keys.Ascending(x => x.DueAt), index => index.ExpireAfter(TimeSpan.FromSeconds(-1))), typeof(ArgumentOutOfRangeException));
        yield return () => ("expiry in part of a second", invoices => invoices.Index(
            keys => keys.Ascending(x => x.DueAt), index => index.ExpireAfter(TimeSpan.FromMilliseconds(1500))), typeof(ArgumentOutOfRangeException));
        yield return () => ("null partial", invoices => invoices.Index(
            keys => keys.Ascending(x => x.DueAt), index => index.Partial((Expression<Func<Invoice, bool>>)null!)), typeof(ArgumentNullException));
        yield return () => ("null collation", invoices => invoices.Index(
            keys => keys.Ascending(x => x.DueAt), index => index.Collation(null!)), typeof(ArgumentNullException));
        yield return () => ("null settings", invoices => invoices.Settings(null!), typeof(ArgumentNullException));
        yield return () => ("null validator", invoices => invoices.Settings(
            settings => settings.Validator((Expression<Func<Invoice, bool>>)null!)), typeof(ArgumentNullException));
        yield return () => ("null collection collation", invoices => invoices.Settings(settings => settings.Collation(null!)),
            typeof(ArgumentNullException));
        yield return () => ("negative clustered expiry", invoices => invoices.Settings(
            settings => settings.Clustered(TimeSpan.FromSeconds(-1))), typeof(ArgumentOutOfRangeException));
    }

    private static VaultHost<BillingVault> Host(Action<IVaultCollectionBuilder<Invoice, int>> invoices) =>
        new(vault => vault.Collection(x => x.Invoices, invoices));

    private static VaultHost<BillingVault> EncryptedHost(Action<IVaultCollectionBuilder<Invoice, int>> invoices) =>
        new(vault =>
        {
            ((VaultModelBuilderBase)vault).UseEncryption(new PlainEncryption());
            vault.Collection(x => x.Invoices, collection =>
            {
                ((CollectionModelBuilder<Invoice>)collection).Encrypt((Expression<Func<Invoice, string>>)(x => x.TaxNumber), new object());
                invoices(collection);
            });
        });

    private static VaultModel Model(VaultHost<BillingVault> host) =>
        host.Services.GetRequiredService<VaultModelProvider<BillingVault>>().Model;

    private static ICollectionModel Collection(VaultHost<BillingVault> host) => Model(host).Collections.Single();

    private static IEnumerable<object> Indexes(VaultHost<BillingVault> host) =>
        Collection(host).Indexes.Select(index => new { index.Name, index.Listed });
}

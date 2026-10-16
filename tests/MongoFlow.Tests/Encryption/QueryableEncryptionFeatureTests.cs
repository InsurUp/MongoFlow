using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Encryption;
using MongoFlow.Encryption;

namespace MongoFlow.Tests.Encryption;

/// <summary>
/// <see cref="QueryableEncryptionFeature"/>, as a vault's model is built with it:
/// <list type="number">
/// <item>the vault reads and writes through a client that encrypts, made from the one it was given, which vaults with the
/// same settings share, and saves with collection commands;</item>
/// <item>a member can't be encrypted where MongoFlow or the server relies on reading it: the <c>_id</c>, the key, what a
/// feature filters or changes, or another encrypted member;</item>
/// <item>the documents must ignore the <c>__safeContent__</c> the server adds, and keep no extra elements;</item>
/// <item>encrypted members need the vault to encrypt, which needs a key vault and a KMS, on a client that doesn't encrypt
/// itself.</item>
/// </list>
/// </summary>
public partial class QueryableEncryptionFeatureTests
{
    [Test]
    public async Task Build_EncryptedVault_ReadsAndWritesThroughAClientThatEncrypts()
    {
        // Arrange
        var services = new ServiceCollection().AddSingleton(Offline.Client);
        services.AddMongoVault<CustomerVault>(vault => vault
            .UseDatabase(Offline.DatabaseName)
            .UseQueryableEncryption(Encryption())
            .Collection(x => x.Customers, customers => customers.Encrypt(x => x.Ssn, EncryptedQuery.Equality)));
        services.AddMongoVault<StrictVault>(vault => vault.UseDatabase(Offline.DatabaseName).UseQueryableEncryption(Encryption()));
        services.AddMongoVault<KeepingVault>(vault => vault
            .UseDatabase(Offline.DatabaseName)
            .UseQueryableEncryption(qe => qe.KeyVault("encryption", "__keyVault").Kms(new LocalKms(Enumerable.Repeat((byte)1, 96).ToArray()))));
        await using var provider = services.BuildServiceProvider();

        // Act
        var customers = provider.GetRequiredService<VaultModelProvider<CustomerVault>>().Model;
        var stricts = provider.GetRequiredService<VaultModelProvider<StrictVault>>().Model;
        var keepings = provider.GetRequiredService<VaultModelProvider<KeepingVault>>().Model;

        // Assert
        await Verify(new
        {
            Encrypts = customers.Client.Settings.AutoEncryptionOptions is { BypassQueryAnalysis: true },
            Source = ReferenceEquals(customers.SourceDatabase.Client, Offline.Client),
            customers.WritesPerCommand,
            Encrypted = customers.Collections[0].Encryption?.Paths,
            SameSettingsShareTheClient = ReferenceEquals(customers.Client, stricts.Client),
            OtherKeysDont = ReferenceEquals(customers.Client, keepings.Client)
        });
    }

    [Test]
    [Arguments("Local")]
    [Arguments("Aws")]
    [Arguments("AwsFromEnvironment")]
    [Arguments("Azure")]
    [Arguments("AzureFromManagedIdentity")]
    [Arguments("Gcp")]
    [Arguments("GcpFromEnvironment")]
    [Arguments("Kmip")]
    public async Task Build_EachKmsProvider_MakesAClientThatEncrypts(string provider)
    {
        // Arrange — the driver and libmongocrypt check each provider's settings when the client that encrypts is made.
        KmsProvider kms = provider switch
        {
            "Local" => new LocalKms(MasterKey),
            "Aws" => new AwsKms(new AwsMasterKey("eu-central-1", "arn:aws:kms:eu-central-1:111122223333:key/0"), "AKIA", "secret"),
            "AwsFromEnvironment" => AwsKms.FromEnvironment(new AwsMasterKey("eu-central-1", "arn:aws:kms:eu-central-1:111122223333:key/0")),
            "Azure" => new AzureKms(new AzureMasterKey("vault.vault.azure.net", "key"), "tenant", "client", "secret"),
            "AzureFromManagedIdentity" => AzureKms.FromManagedIdentity(new AzureMasterKey("vault.vault.azure.net", "key")),
            "Gcp" => new GcpKms(new GcpMasterKey("project", "global", "ring", "key"), "account@project.iam.gserviceaccount.com", "cHJpdmF0ZQ=="),
            "GcpFromEnvironment" => GcpKms.FromEnvironment(new GcpMasterKey("project", "global", "ring", "key")),
            _ => new KmipKms("kmip.example.com:5696", new SslSettings())
        };
        await using var host = new VaultHost<CustomerVault>(vault => vault
            .UseQueryableEncryption(qe => qe.KeyVault("encryption", "__keyVault").Kms(kms))
            .Collection(x => x.Customers, customers => customers.Encrypt(x => x.Ssn, EncryptedQuery.Equality)));

        // Act
        var model = host.Services.GetRequiredService<VaultModelProvider<CustomerVault>>().Model;

        // Assert
        await Assert.That(model.Client.Settings.AutoEncryptionOptions!.KmsProviders.Keys).IsEquivalentTo([kms.Name]);
    }

    [Test]
    [Arguments("Id")]
    [Arguments("Key")]
    [Arguments("SoftDeleteFlag")]
    [Arguments("Tenant")]
    [Arguments("ConcurrencyToken")]
    [Arguments("Twice")]
    [Arguments("InsideAnother")]
    public async Task Build_MemberThatCantBeEncrypted_ThrowsVaultConfigurationException(string member)
    {
        // Arrange
        await using var host = member == "Key"
            ? (IAsyncDisposable)new VaultHost<NumberedVault>(vault => vault
                .UseQueryableEncryption(Encryption())
                .Collection(x => x.Customers, customers => customers.Key(x => x.Number).Encrypt(x => x.Number)))
            : new VaultHost<CustomerVault>(vault =>
            {
                vault.UseQueryableEncryption(Encryption())
                    .UseSoftDelete((IFeatured x) => x.IsDeleted)
                    .UseMultiTenancy((IFeatured x) => x.TenantId, _ => "tenant")
                    .UseConcurrencyToken((IFeatured x) => x.Version);

                vault.Collection(x => x.Customers, customers => _ = member switch
                {
                    "Id" => customers.Encrypt(x => x.Id),
                    "SoftDeleteFlag" => customers.Encrypt(x => x.IsDeleted),
                    "Tenant" => customers.Encrypt(x => x.TenantId),
                    "ConcurrencyToken" => customers.Encrypt(x => x.Version),
                    "Twice" => customers.Encrypt(x => x.Ssn).Encrypt(x => x.Ssn, EncryptedQuery.Equality),
                    _ => customers.Encrypt(x => x.Address).Encrypt(x => x.Address!.Street)
                });
            });

        // Act & Assert
        await Throws(() => Resolve(host)).IgnoreStackTrace().UseParameters(member);
    }

    [Test]
    public async Task Build_DocumentsThatDontIgnoreExtraElements_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<StrictVault>(vault => vault
            .UseQueryableEncryption(Encryption())
            .Collection(x => x.Stricts, stricts => stricts.Encrypt(x => x.Ssn)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_DocumentsKeepingExtraElements_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<KeepingVault>(vault => vault
            .UseQueryableEncryption(Encryption())
            .Collection(x => x.Keepings, keepings => keepings.Encrypt(x => x.Ssn)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_DocumentsWithoutAClassMap_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<RawVault>(vault => vault
            .UseQueryableEncryption(Encryption())
            .Collection(x => x.Raw, raw => raw.Encrypt(x => x["Ssn"], bsonType: MongoDB.Bson.BsonType.String)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_EncryptedMembersWithoutUseQueryableEncryption_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<CustomerVault>(vault => vault
            .Collection(x => x.Customers, customers => customers.Encrypt(x => x.Ssn)));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    [Arguments("KeyVault")]
    [Arguments("Kms")]
    public async Task Build_EncryptionMissingASetting_ThrowsVaultConfigurationException(string missing)
    {
        // Arrange
        await using var host = new VaultHost<CustomerVault>(vault => vault.UseQueryableEncryption(qe =>
        {
            if (missing != "KeyVault")
            {
                qe.KeyVault("encryption", "__keyVault");
            }

            if (missing != "Kms")
            {
                qe.Kms(new LocalKms(MasterKey));
            }
        }));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace().UseParameters(missing);
    }

    [Test]
    public async Task Build_ClientThatEncryptsItself_ThrowsVaultConfigurationException()
    {
        // Arrange — the client MongoFlow makes from it would encrypt twice.
        try
        {
            MongoClientSettings.Extensions.AddAutoEncryption();
        }
        catch (MongoConfigurationException)
        {
            // Registered already, by MongoFlow.Encryption or another test.
        }

        var settings = Offline.Client.Settings.Clone();
        settings.AutoEncryptionOptions = new AutoEncryptionOptions(new CollectionNamespace("encryption", "__keyVault"),
            new Dictionary<string, IReadOnlyDictionary<string, object>> { ["local"] = new Dictionary<string, object> { ["key"] = MasterKey } },
            bypassQueryAnalysis: true);
        var client = new MongoClient(settings);
        await using var host = new VaultHost<CustomerVault>(vault => vault
            .UseDatabase(client.GetDatabase(Offline.DatabaseName))
            .UseQueryableEncryption(Encryption()));

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    private static Action<QueryableEncryptionOptions> Encryption() =>
        qe => qe.KeyVault("encryption", "__keyVault").Kms(new LocalKms(MasterKey));

    private static object Resolve(IAsyncDisposable host) =>
        host switch
        {
            VaultHost<NumberedVault> numbered => numbered.Vault,
            VaultHost<CustomerVault> customers => customers.Vault,
            _ => throw new ArgumentOutOfRangeException(nameof(host))
        };
}

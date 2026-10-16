using MongoDB.Bson;
using MongoDB.Driver;
using MongoFlow.Encryption;

namespace MongoFlow.Tests.Encryption;

/// <summary>
/// The KMS providers (<see cref="KmsProvider"/>) give the driver what it takes for each KMS:
/// <list type="number">
/// <item>its name, its credentials by the names the driver reads, none when they come from the environment, and the
/// master key data keys are created under;</item>
/// <item>what can't make a working KMS fails when the provider is made: a local key of another length, a missing
/// credential or master key part.</item>
/// </list>
/// </summary>
public class KmsProviderTests
{
    private const string Arn = "arn:aws:kms:eu-central-1:111122223333:key/0000";

    [Test]
    public async Task Providers_EachKms_GiveTheDriverWhatItTakes()
    {
        // Act
        var providers = new KmsProvider[]
        {
            new LocalKms(new byte[96]),
            new AwsKms(new AwsMasterKey("eu-central-1", Arn) { Endpoint = "vpce.kms.example.com" }, "AKIA", "secret", "token"),
            AwsKms.FromEnvironment(new AwsMasterKey("eu-central-1", Arn)),
            new AzureKms(new AzureMasterKey("my-vault.vault.azure.net", "mongoflow") { KeyVersion = "2" }, "tenant", "client",
                "secret", "login.microsoftonline.us"),
            AzureKms.FromManagedIdentity(new AzureMasterKey("my-vault.vault.azure.net", "mongoflow")),
            new GcpKms(new GcpMasterKey("project", "europe-west1", "ring", "mongoflow") { KeyVersion = "1", Endpoint = "kms.example.com" },
                "mongoflow@project.iam.gserviceaccount.com", "cHJpdmF0ZQ==", "oauth.example.com"),
            GcpKms.FromEnvironment(new GcpMasterKey("project", "global", "ring", "mongoflow")),
            new KmipKms("kmip.example.com:5696", new SslSettings(), new KmipMasterKey { KeyId = "1", Endpoint = "other:5696", Delegated = true }),
            new KmipKms("kmip.example.com", new SslSettings())
        }.Select(provider => new
        {
            provider.Name,
            Credentials = provider.Credentials.ToDictionary(setting => setting.Key,
                setting => setting.Value is byte[] bytes ? $"{bytes.Length} bytes" : setting.Value),
            provider.MasterKey,
            Tls = provider.Tls is not null
        });

        // Assert
        await Verify(providers).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task FromBase64_LocalKey_ReadsIt()
    {
        // Arrange
        var key = Convert.ToBase64String(Enumerable.Range(0, 96).Select(i => (byte)i).ToArray());

        // Act
        var kms = LocalKms.FromBase64(key);

        // Assert
        await Assert.That(((byte[])kms.Credentials["key"])[95]).IsEqualTo((byte)95);
    }

    [Test]
    [Arguments("ShortLocalKey")]
    [Arguments("LocalKeyNotInBase64")]
    [Arguments("AwsWithoutASecret")]
    [Arguments("AwsMasterKeyWithoutARegion")]
    [Arguments("AzureWithoutATenant")]
    [Arguments("AzureMasterKeyWithoutAName")]
    [Arguments("GcpWithoutAPrivateKey")]
    [Arguments("GcpMasterKeyWithoutAKeyRing")]
    [Arguments("KmipWithoutAnEndpoint")]
    [Arguments("KmipWithoutTls")]
    public async Task Constructor_WhatCantMakeAWorkingKms_Throws(string provider)
    {
        // Arrange
        Func<KmsProvider> create = provider switch
        {
            "ShortLocalKey" => () => new LocalKms(new byte[32]),
            "LocalKeyNotInBase64" => () => LocalKms.FromBase64("not base64!"),
            "AwsWithoutASecret" => () => new AwsKms(new AwsMasterKey("eu-central-1", Arn), "AKIA", ""),
            "AwsMasterKeyWithoutARegion" => () => AwsKms.FromEnvironment(new AwsMasterKey(" ", Arn)),
            "AzureWithoutATenant" => () => new AzureKms(new AzureMasterKey("vault", "key"), "", "client", "secret"),
            "AzureMasterKeyWithoutAName" => () => AzureKms.FromManagedIdentity(new AzureMasterKey("vault", "")),
            "GcpWithoutAPrivateKey" => () => new GcpKms(new GcpMasterKey("project", "global", "ring", "key"), "email", ""),
            "GcpMasterKeyWithoutAKeyRing" => () => GcpKms.FromEnvironment(new GcpMasterKey("project", "global", "", "key")),
            "KmipWithoutAnEndpoint" => () => new KmipKms("", new SslSettings()),
            _ => () => new KmipKms("kmip.example.com", null!)
        };

        // Act & Assert
        await Throws(create).IgnoreStackTrace().UseParameters(provider);
    }
}

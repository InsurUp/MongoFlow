using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="IMigrationBuilder{TVault}"/>, as a vault's migrations are declared:
/// <list type="number">
/// <item>migrations are added one by one or from an assembly, which adds only the vault's concrete ones, each once;</item>
/// <item>applied migrations are recorded in <c>migrations</c> unless a collection is named, the vault's name beating a
/// default configuration's;</item>
/// <item>a vault that declares none has none, and invalid arguments fail when the model is built, as does a
/// <see cref="MongoVersionAttribute"/> that isn't a semantic version or has no migrations to reach it.</item>
/// </list>
/// </summary>
public partial class MigrationBuilderTests
{
    [Test]
    public async Task AddFromAssemblyOf_MigrationsOfSeveralVaults_AddsOnlyTheVaultsConcreteOnes()
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(vault => vault.Migrations(m => m.AddFromAssemblyOf<ArchiveVault>()));

        // Act
        var migrations = Migrations(host);

        // Assert
        await Verify(new { Types = migrations!.Types.Select(type => type.Name).Order(), migrations.CollectionName });
    }

    [Test]
    public async Task Add_SameMigrationTwice_AddsItOnce()
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(vault => vault
            .Migrations(m => m.Add<CreateArchive>().Add<CreateArchive>())
            .Migrations(m => m.Add<CreateArchive>()));

        // Act
        var migrations = Migrations(host);

        // Assert
        await Assert.That(migrations!.Types).IsEquivalentTo([typeof(CreateArchive)]);
    }

    [Test]
    public async Task Build_NoMigrations_HasNone()
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(vault => vault.Migrations(m => m.CollectionName("history")));

        // Act
        var migrations = Migrations(host);

        // Assert
        await Assert.That(migrations).IsNull();
    }

    [Test]
    public async Task CollectionName_SetByADefaultAndByTheVault_IsTheVaults()
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(
            vault => vault.Migrations(m => m.Add<CreateArchive>().CollectionName("own_history")),
            services => services.AddDefaultVaultConfiguration(typeof(HistoryDefault<>)));

        // Act
        var migrations = Migrations(host);

        // Assert
        await Assert.That(migrations!.CollectionName).IsEqualTo("own_history");
    }

    [Test]
    public async Task CollectionName_SetOnlyByADefault_IsTheDefaults()
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(
            vault => vault.Migrations(m => m.Add<CreateArchive>()),
            services => services.AddDefaultVaultConfiguration(typeof(HistoryDefault<>)));

        // Act
        var migrations = Migrations(host);

        // Assert
        await Assert.That(migrations!.CollectionName).IsEqualTo(HistoryDefault<ArchiveVault>.CollectionName);
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task CollectionName_Blank_ThrowsArgumentException(string name)
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(vault => vault.Migrations(m => m.CollectionName(name)));

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task CollectionName_Null_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(vault => vault.Migrations(m => m.CollectionName(null!)));

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Migrations_NullConfigure_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = new VaultHost<ArchiveVault>(vault => vault.Migrations(null!));

        // Act & Assert
        await Assert.That(() => host.Vault).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Build_MongoVersionThatIsntASemanticVersion_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<LooseVersionVault>();

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    [Test]
    public async Task Build_MongoVersionWithoutMigrations_ThrowsVaultConfigurationException()
    {
        // Arrange
        await using var host = new VaultHost<UnmigratedVault>();

        // Act & Assert
        await Throws(() => host.Vault).IgnoreStackTrace();
    }

    private static MigrationModel? Migrations(VaultHost<ArchiveVault> host) =>
        host.Services.GetRequiredService<VaultModelProvider<ArchiveVault>>().Model.Migrations;
}

using MongoDB.Bson;
using MongoFlow.Identity;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

/// <summary>
/// ASP.NET Core Identity's users, roles and tokens, through MongoFlow.Identity. It's a vault like any other: accounts sit
/// above tenants, so the platform rules are skipped at registration, and the vault adds the soft delete it wants.
/// </summary>
public sealed class PlatformUserVault : IdentityMongoVault<PlatformUser, PlatformRole, ObjectId>,
    IConfigurableVault<PlatformUserVault>
{
    public static void Configure(IVaultBuilder<PlatformUserVault> vault) =>
        vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted);
}

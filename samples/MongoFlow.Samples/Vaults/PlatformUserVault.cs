using MongoFlow.Samples.Identity;

namespace MongoFlow.Samples.Vaults;

public sealed class PlatformUser : UserAccount
{
    public string? DisplayName { get; set; }
}

public sealed class PlatformUserVault : UserVault<PlatformUserVault, PlatformUser>;

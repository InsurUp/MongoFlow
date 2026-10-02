using MongoFlow.Identity;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Vaults;

/// <summary>A platform account, stored by MongoFlow.Identity. Deleting one keeps it, marked deleted.</summary>
public sealed class PlatformUser : MongoUser, ISoftDeletable
{
    public string? DisplayName { get; set; }

    public bool IsDeleted { get; set; }
}

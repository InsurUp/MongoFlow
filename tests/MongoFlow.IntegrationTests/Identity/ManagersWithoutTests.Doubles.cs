using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

// Users and roles that soft delete applies to, in a vault of their own.
public partial class ManagersWithoutTests
{
    public interface IArchivable
    {
        bool IsDeleted { get; set; }
    }

    public sealed class ArchivableUser : MongoUser, IArchivable
    {
        public bool IsDeleted { get; set; }
    }

    public sealed class ArchivableRole : MongoRole, IArchivable
    {
        public bool IsDeleted { get; set; }
    }

    public sealed class ArchiveVault : IdentityMongoVault<ArchivableUser, ArchivableRole, ObjectId>;
}

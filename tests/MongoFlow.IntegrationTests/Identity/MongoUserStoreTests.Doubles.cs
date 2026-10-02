using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

// The store, with the lookups Identity's managers don't call made callable.
public partial class MongoUserStoreTests
{
    /// <summary>The user store with two protected lookups exposed, as a subclass would call them.</summary>
    public sealed class ExposedUserStore(UsersVault vault)
        : MongoUserStore<UsersVault, MongoUser, MongoRole, ObjectId>(vault, new IdentityErrorDescriber())
    {
        public Task<IdentityUserLogin<ObjectId>?> FindLoginAsync(ObjectId userId,
            string loginProvider,
            string providerKey) =>
            FindUserLoginAsync(userId, loginProvider, providerKey, CancellationToken.None);

        public Task<IdentityUserRole<ObjectId>?> FindRoleOfAsync(ObjectId userId,
            ObjectId roleId) =>
            FindUserRoleAsync(userId, roleId, CancellationToken.None);
    }
}

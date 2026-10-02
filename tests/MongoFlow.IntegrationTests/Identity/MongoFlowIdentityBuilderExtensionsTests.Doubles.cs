using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

// Users and roles Identity accepts but the MongoFlow stores don't, and a user the vault doesn't store.
public partial class MongoFlowIdentityBuilderExtensionsTests
{
    public sealed class PlainUser : IdentityUser<ObjectId>;

    public sealed class PlainRole : IdentityRole<ObjectId>;

    public sealed class OtherUser : MongoUser;
}

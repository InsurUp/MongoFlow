using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

/// <summary>An app's Identity vault with plain users and roles, keyed by <c>ObjectId</c>.</summary>
public sealed class UsersVault : IdentityMongoVault;

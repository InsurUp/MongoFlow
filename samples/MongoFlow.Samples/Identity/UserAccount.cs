using MongoDB.Bson;

// Stands in for a separate identity package built on MongoFlow, the way MongoFlow.Identity is.
namespace MongoFlow.Samples.Identity;

public abstract class UserAccount
{
    public ObjectId Id { get; set; }

    public required string UserName { get; set; }

    public required string NormalizedEmail { get; set; }

    public string? PasswordHash { get; set; }
}

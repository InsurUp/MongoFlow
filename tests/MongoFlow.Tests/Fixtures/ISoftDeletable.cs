namespace MongoFlow.Tests;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}

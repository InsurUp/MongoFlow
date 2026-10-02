namespace MongoFlow.Samples.Domain;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}

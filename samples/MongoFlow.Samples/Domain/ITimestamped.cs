namespace MongoFlow.Samples.Domain;

public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }
}

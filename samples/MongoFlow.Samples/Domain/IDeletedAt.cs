namespace MongoFlow.Samples.Domain;

/// <summary>Soft deletion that records when, rather than just whether.</summary>
public interface IDeletedAt
{
    DateTime? DeletedAt { get; set; }
}

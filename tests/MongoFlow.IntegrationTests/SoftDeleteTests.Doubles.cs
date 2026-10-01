namespace MongoFlow.IntegrationTests;

// Documents soft-deleted by a flag, a DateTime and a DateTimeOffset, and one that isn't.
public partial class SoftDeleteTests
{
    public interface ISoftDeletable
    {
        bool IsDeleted { get; set; }
    }

    public interface IDeletedAt
    {
        DateTime? DeletedAt { get; set; }
    }

    public interface IRemovedAt
    {
        DateTimeOffset? RemovedAt { get; set; }
    }

    public sealed class Article : ISoftDeletable
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public bool IsDeleted { get; set; }
    }

    public sealed class Comment : IDeletedAt
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";

        public DateTime? DeletedAt { get; set; }
    }

    public sealed class Attachment : IRemovedAt
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public DateTimeOffset? RemovedAt { get; set; }
    }

    public sealed class Tag
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    public sealed class BlogVault : MongoVault
    {
        public IVaultCollection<Article, int> Articles { get; init; } = null!;

        public IVaultCollection<Comment, int> Comments { get; init; } = null!;

        public IVaultCollection<Attachment, int> Attachments { get; init; } = null!;

        public IVaultCollection<Tag, int> Tags { get; init; } = null!;
    }
}

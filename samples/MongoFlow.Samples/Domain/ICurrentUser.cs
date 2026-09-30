namespace MongoFlow.Samples.Domain;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    bool IsPlatformAdmin { get; }

    string? UserId { get; }

    AgencyId? AgencyId { get; }

    ValueTask<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlySet<string>> GetModulesAsync(CancellationToken cancellationToken);
}

using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>Placeholder so the sample host resolves; a real app reads the user from the request.</summary>
public sealed class AnonymousUser : ICurrentUser
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    public bool IsAuthenticated => false;

    public bool IsPlatformAdmin => false;

    public string? UserId => null;

    public AgencyId? AgencyId => null;

    public ValueTask<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(None);

    public ValueTask<IReadOnlySet<string>> GetModulesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(None);
}

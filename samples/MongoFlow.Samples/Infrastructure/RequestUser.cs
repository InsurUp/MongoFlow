using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Infrastructure;

/// <summary>
/// Who the request is for. Authentication would sign the request in; the walkthrough does it for each scope it opens.
/// Until then, the request is anonymous and the permission feature hides every document that needs one.
/// </summary>
public sealed class RequestUser : ICurrentUser
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private IReadOnlySet<string> _permissions = None;
    private IReadOnlySet<string> _modules = None;

    public bool IsAuthenticated => UserId is not null;

    public bool IsPlatformAdmin { get; private set; }

    public string? UserId { get; private set; }

    public AgencyId? AgencyId { get; private set; }

    public void SignIn(string userId,
        AgencyId? agencyId,
        bool isPlatformAdmin,
        IReadOnlySet<string> permissions,
        IReadOnlySet<string> modules)
    {
        UserId = userId;
        AgencyId = agencyId;
        IsPlatformAdmin = isPlatformAdmin;
        _permissions = permissions;
        _modules = modules;
    }

    // A real app would load these from a cache or a service, which is why they're asynchronous.
    public ValueTask<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_permissions);

    public ValueTask<IReadOnlySet<string>> GetModulesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_modules);
}

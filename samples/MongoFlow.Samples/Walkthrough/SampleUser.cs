using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Infrastructure;

namespace MongoFlow.Samples.Walkthrough;

/// <summary>Someone the walkthrough makes requests as.</summary>
public sealed record SampleUser(string UserId,
    AgencyId? AgencyId,
    bool IsPlatformAdmin,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<string> Modules)
{
    public void SignIn(RequestUser user) => user.SignIn(UserId, AgencyId, IsPlatformAdmin, Permissions, Modules);
}

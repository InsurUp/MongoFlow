using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Walkthrough;

/// <summary>A platform admin, and an agent at each of two agencies, which see only their own data.</summary>
public static class SampleUsers
{
    public static readonly AgencyId Acme = new(Guid.Parse("a0000000-0000-0000-0000-000000000001"));

    public static readonly AgencyId Beta = new(Guid.Parse("b0000000-0000-0000-0000-000000000002"));

    private static readonly IReadOnlySet<string> AgentPermissions =
        new HashSet<string> { "policies.read", "claims.read", "customers.read" };

    private static readonly IReadOnlySet<string> AgentModules = new HashSet<string> { "policies", "claims" };

    public static SampleUser Admin { get; } = new("admin", null, IsPlatformAdmin: true, new HashSet<string>(), new HashSet<string>());

    public static SampleUser AcmeAgent { get; } = new("ayse", Acme, IsPlatformAdmin: false, AgentPermissions, AgentModules);

    public static SampleUser BetaAgent { get; } = new("bora", Beta, IsPlatformAdmin: false, AgentPermissions, AgentModules);
}

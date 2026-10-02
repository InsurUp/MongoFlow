using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Interceptors;
using MongoFlow.Samples.Services;
using MongoFlow.Samples.Vaults;
using Semver;

namespace MongoFlow.Samples.Walkthrough;

/// <summary>
/// Runs the platform's services step by step, as requests from its users would, and logs what each step shows. Read it
/// top to bottom; each step's services are in <c>Services/</c>.
/// </summary>
public sealed class PlatformWalkthrough(IServiceProvider services,
    TimeProvider clock,
    ILogger<PlatformWalkthrough> log)
{
    private readonly SampleRequests _requests = new(services);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var ada = await SeedAsync(cancellationToken);
        await ReadAsync(ada, cancellationToken);
        await TrackChangesAsync(cancellationToken);
        await ResolveAConflictAsync(cancellationToken);
        var claim = await FileClaimsAsync(ada, cancellationToken);
        await DecideAClaimOnceAsync(claim, cancellationToken);
        await WriteSetsAndSoftDeleteAsync(cancellationToken);
        await RenewInParallelAsync(cancellationToken);
        await ManageAccountsAsync();
        await ReadTheAuditTrailAsync(cancellationToken);
        await MigrateBackAndForthAsync(cancellationToken);
    }

    /// <summary>Agencies, a customer with a consent, and policies: inserts, stamped with the tenant and timestamps.</summary>
    private async Task<ObjectId> SeedAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("1. Seed: the admin registers two agencies; each agent registers a customer and issues policies");

        await _requests.AsAsync(SampleUsers.Admin, async request =>
        {
            var agencies = request.GetRequiredService<AgencyService>();
            await agencies.RegisterAsync(new Agency { AgencyId = SampleUsers.Acme, Name = "Acme Insurance", Modules = ["policies", "claims"] }, cancellationToken);
            await agencies.RegisterAsync(new Agency { AgencyId = SampleUsers.Beta, Name = "Beta Brokers", Modules = ["policies"] }, cancellationToken);
        });

        var now = clock.GetUtcNow().UtcDateTime;
        var ada = await _requests.AsAsync(SampleUsers.AcmeAgent, async request =>
        {
            var customer = await request.GetRequiredService<CustomerService>().RegisterAsync("Ada Lovelace", "ada@example.com", cancellationToken);
            await request.GetRequiredService<CustomerService>().GiveConsentAsync(customer.Id, ConsentPurpose.Marketing, cancellationToken);

            var policies = request.GetRequiredService<PolicyService>();
            await policies.IssueAsync(NewPolicy("P-1001", SampleUsers.AcmeAgent, customer.Id, 1200, now.AddMonths(-3), now.AddMonths(9)), cancellationToken);
            await policies.IssueAsync(NewPolicy("P-1002", SampleUsers.AcmeAgent, customer.Id, 800, now.AddMonths(-6), now.AddMonths(6)), cancellationToken);
            await policies.IssueAsync(NewPolicy("P-1003", SampleUsers.AcmeAgent, customer.Id, 450, now.AddMonths(-13), now.AddMonths(-1)), cancellationToken);
            await policies.IssueAsync(NewPolicy("P-1004", SampleUsers.AcmeAgent, customer.Id, 300, now.AddMonths(-1), now.AddMonths(11)), cancellationToken);

            return customer.Id;
        });

        await _requests.AsAsync(SampleUsers.BetaAgent, async request =>
        {
            var customer = await request.GetRequiredService<CustomerService>().RegisterAsync("Bob Dylan", "bob@example.com", cancellationToken);
            await request.GetRequiredService<PolicyService>()
                .IssueAsync(NewPolicy("P-2001", SampleUsers.BetaAgent, customer.Id, 950, now.AddMonths(-2), now.AddMonths(10)), cancellationToken);
        });

        log.LogInformation("   Acme registered Ada ({CustomerId}) and issued P-1001 to P-1004; Beta issued P-2001", ada);

        return ada;
    }

    /// <summary>Reads, each starting from the collection's query filters: tenant, soft delete, permissions and modules.</summary>
    private async Task ReadAsync(ObjectId ada, CancellationToken cancellationToken)
    {
        log.LogInformation("2. Reads: by key, LINQ, find and aggregation, each limited to the agent's agency");

        await _requests.AsAsync(SampleUsers.AcmeAgent, async request =>
        {
            var policies = request.GetRequiredService<PolicyService>();

            var policy = (await policies.FindAsync("P-1001", cancellationToken))!;
            log.LogInformation("   P-1001: {Status}, premium {Premium}, agency stamped {AgencyId}, created {CreatedAt:u}",
                policy.Status, policy.Premium, policy.AgencyId?.Value, policy.CreatedAt);

            var others = await policies.FindAsync("P-2001", cancellationToken);
            log.LogInformation("   P-2001 is Beta's, so Acme finds {Found}", others is null ? "nothing" : "it");

            var active = await policies.ActiveForCustomerAsync(ada, cancellationToken);
            var (page, total) = await policies.PageAsync(page: 0, size: 2, cancellationToken);
            log.LogInformation("   Ada has {Active} active policies; the first page holds {Page} of {Total}", active.Count, page.Count, total);

            var expiring = await policies.ExpiringAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
            log.LogInformation("   Ended already: {Ended}", string.Join(", ", expiring.Select(p => p.PolicyNumber)));

            var summaries = await policies.SummariesAsync(cancellationToken);
            var premiums = await policies.PremiumByStatusAsync(cancellationToken);
            log.LogInformation("   Projected {Summaries} summaries; premium by status: {Premiums}",
                summaries.Count, string.Join(", ", premiums.Select(total => $"{total.Status} {total.Total}")));

            var customers = request.GetRequiredService<CustomerService>();
            log.LogInformation("   Consents, by a composite key: marketing {Marketing}, data sharing {DataSharing}",
                await customers.HasConsentAsync(ada, ConsentPurpose.Marketing, cancellationToken),
                await customers.HasConsentAsync(ada, ConsentPurpose.DataSharing, cancellationToken));
        });

        await _requests.AsAsync(SampleUsers.Admin, async request =>
        {
            var agency = await request.GetRequiredService<AgencyService>().FindAsync(SampleUsers.Acme, cancellationToken);
            log.LogInformation("   The admin finds agency {Name} by its strongly typed key", agency?.Name);
        });
    }

    /// <summary>A tracked policy, changed where it was read: the save writes only what changed.</summary>
    private async Task TrackChangesAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("3. Change tracking: cancelling P-1004 changes the policy read; the save writes its status");

        await _requests.AsAsync(SampleUsers.AcmeAgent, request =>
            request.GetRequiredService<PolicyService>().CancelAsync("P-1004", cancellationToken));

        var policy = await _requests.AsAsync(SampleUsers.AcmeAgent, request =>
            request.GetRequiredService<PolicyService>().FindAsync("P-1004", cancellationToken));

        log.LogInformation("   P-1004 is {Status}, at version {Version}, updated {UpdatedAt:u}", policy!.Status, policy.Version, policy.UpdatedAt);
    }

    /// <summary>Two requests change one policy; the second save meets the concurrency token and reads again.</summary>
    private async Task ResolveAConflictAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("4. Concurrency: two requests change P-1001, and the one that saves second has to read again");

        await using (var slow = _requests.Begin(SampleUsers.AcmeAgent))
        {
            var policy = (await slow.ServiceProvider.GetRequiredService<PolicyService>().FindAsync("P-1001", cancellationToken))!;

            await _requests.AsAsync(SampleUsers.AcmeAgent, request => request.GetRequiredService<PolicyService>()
                .RenewAsync("P-1001", clock.GetUtcNow().UtcDateTime.AddYears(1), cancellationToken));

            policy.Premium = 1300;
            try
            {
                await slow.ServiceProvider.GetRequiredService<IPolicyVault>().SaveAsync(cancellationToken);
            }
            catch (ConcurrencyException conflict)
            {
                log.LogInformation("   The slow request's save failed: {Message} Still stored: {Exists}", conflict.Message, conflict.DocumentExists);
            }
        }

        await _requests.AsAsync(SampleUsers.AcmeAgent, async request =>
        {
            var policies = request.GetRequiredService<PolicyService>();
            var policy = (await policies.FindAsync("P-1001", cancellationToken))!;
            policy.Premium = 1300;
            await request.GetRequiredService<IPolicyVault>().SaveAsync(cancellationToken);

            log.LogInformation("   Read again and saved: premium {Premium}, version {Version}", policy.Premium, policy.Version);
        });
    }

    /// <summary>A transaction across two vaults: committed, then rolled back when a rule fails after the first save.</summary>
    private async Task<Guid> FileClaimsAsync(ObjectId ada, CancellationToken cancellationToken)
    {
        log.LogInformation("5. Transactions: claims are filed across two vaults; the one over Ada's limit rolls back");

        Guid[] claims = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        string[] policies = ["P-1001", "P-1002", "P-1001"];

        for (var i = 0; i < claims.Length; i++)
        {
            var claim = new Claim { Id = claims[i], PolicyNumber = policies[i], Amount = 250 * (i + 1) };
            var filed = await _requests.AsAsync(SampleUsers.AcmeAgent, request =>
                request.GetRequiredService<ClaimService>().FileAsync(claim, cancellationToken));

            log.LogInformation("   Claim of {Amount} on {Policy}: {Outcome}", claim.Amount, claim.PolicyNumber, filed ? "filed" : "refused, rolled back");
        }

        await _requests.AsAsync(SampleUsers.AcmeAgent, async request =>
        {
            var customer = await request.GetRequiredService<CustomerService>().FindAsync(ada, cancellationToken);
            var onP1001 = await request.GetRequiredService<ClaimService>().ForPolicyAsync("P-1001", cancellationToken);
            log.LogInformation("   Ada has {OpenClaims} open claims, and P-1001 has {Claims}: the refused one isn't stored",
                customer!.OpenClaims, onP1001.Count);
        });

        return claims[0];
    }

    /// <summary>A write condition: a claim is decided once, checked by the server in the write itself.</summary>
    private async Task DecideAClaimOnceAsync(Guid claim, CancellationToken cancellationToken)
    {
        log.LogInformation("6. Write conditions: a claim can be decided only while it's open");

        await _requests.AsAsync(SampleUsers.AcmeAgent, request =>
            request.GetRequiredService<ClaimService>().DecideAsync(claim, ClaimStatus.Approved, cancellationToken));
        log.LogInformation("   Approved claim {Claim}", claim);

        try
        {
            await _requests.AsAsync(SampleUsers.AcmeAgent, request =>
                request.GetRequiredService<ClaimService>().DecideAsync(claim, ClaimStatus.Rejected, cancellationToken));
        }
        catch (ClaimAlreadyDecidedException decided)
        {
            log.LogInformation("   Rejecting it failed: {Message}", decided.Message);
        }
    }

    /// <summary>A set-based update, a soft delete by key, and a platform count with features switched off.</summary>
    private async Task WriteSetsAndSoftDeleteAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("7. Set-based writes and soft delete: ended policies expire, and P-1003 is deleted but kept");

        await _requests.AsAsync(SampleUsers.AcmeAgent, async request =>
        {
            var policies = request.GetRequiredService<PolicyService>();
            var expired = await policies.ExpireEndedAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
            await policies.DeleteAsync("P-1003", cancellationToken);

            var (_, visible) = await policies.PageAsync(page: 0, size: 10, cancellationToken);
            log.LogInformation("   Expired {Expired}; Acme now sees {Visible} policies", expired, visible);
        });

        var everything = await _requests.AsAsync(SampleUsers.Admin, request =>
            request.GetRequiredService<PolicyService>().CountEverythingAsync(cancellationToken));
        log.LogInformation("   With multi-tenancy and soft delete off, the admin counts {Everything}", everything);
    }

    /// <summary>Reads from parallel tasks on one vault instance, then one save of everything they changed.</summary>
    private async Task RenewInParallelAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("8. Parallel reads: P-1001 and P-1002 are read at once, renewed, and saved together");

        var renewed = await _requests.AsAsync(SampleUsers.AcmeAgent, request => request.GetRequiredService<RenewalService>()
            .RenewAsync(["P-1001", "P-1002"], clock.GetUtcNow().UtcDateTime.AddYears(2), cancellationToken));

        log.LogInformation("   Renewed {Renewed} policies in one save", renewed);
    }

    /// <summary>ASP.NET Core Identity's managers on MongoFlow.Identity, with a vault feature switched off.</summary>
    private async Task ManageAccountsAsync()
    {
        log.LogInformation("9. Identity: an account is created, deleted, which keeps it, and restored");

        await _requests.AsAsync(SampleUsers.Admin, async request =>
        {
            var accounts = request.GetRequiredService<AccountService>();
            var account = await accounts.CreateAsync("ayse@acme.example", "Sample-Pa55word", "agent");
            log.LogInformation("   Created {Email} with roles {Roles}", account.Email, string.Join(", ", await accounts.RolesAsync(account)));

            await accounts.DeactivateAsync(account);
            log.LogInformation("   Deleted; the manager finds {Found}", await accounts.FindAsync("ayse@acme.example") is null ? "nothing" : "it");

            var restored = await accounts.RestoreAsync("ayse@acme.example");
            log.LogInformation("   Restored through a manager with soft delete off: {Email}", restored?.Email);
        });
    }

    /// <summary>What the audit interceptor wrote into another vault, in the transaction of each change.</summary>
    private async Task ReadTheAuditTrailAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("10. Audit: every change to Acme's policies was recorded in the audit vault");

        var entries = await _requests.AsAsync(SampleUsers.AcmeAgent, request =>
            request.GetRequiredService<AuditReader>().RecentAsync("policies", cancellationToken));

        log.LogInformation("   {Count} entries, newest first: {Actions}", entries.Count, string.Join(", ", entries.Select(entry => entry.Action)));
    }

    /// <summary>The policy vault's migrations reverted down to a version, then applied again.</summary>
    private async Task MigrateBackAndForthAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("11. Migrations: the policy vault goes back to 1.1.0, reverting what came after, and forward again");

        var migrator = services.GetRequiredService<IVaultMigrator>();
        var before = await migrator.GetVersionAsync<PolicyVault>(cancellationToken);

        await migrator.MigrateAsync<PolicyVault>(new SemVersion(1, 1, 0), cancellationToken);
        var reverted = await migrator.GetVersionAsync<PolicyVault>(cancellationToken);

        await migrator.MigrateAsync<PolicyVault>(cancellationToken: cancellationToken);
        var after = await migrator.GetVersionAsync<PolicyVault>(cancellationToken);

        log.LogInformation("   Version {Before}, then {Reverted}, then {After}", before, reverted, after);
    }

    private static Policy NewPolicy(string number,
        SampleUser owner,
        ObjectId customerId,
        decimal premium,
        DateTime startsAt,
        DateTime endsAt) =>
        new()
        {
            PolicyNumber = number,
            CustomerId = customerId,
            Status = PolicyStatus.Active,
            Premium = premium,
            StartsAt = startsAt,
            EndsAt = endsAt,
            OwnerUserId = owner.UserId
        };
}

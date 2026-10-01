using Microsoft.Extensions.Logging;
using Semver;

namespace MongoFlow;

/// <summary>What <c>MongoFlow.Migrations</c> logs; the events are in <see cref="MongoFlowLogEvents.Migrations"/>.</summary>
internal static partial class MigrationLog
{
    [LoggerMessage(EventId = MongoFlowLogEvents.Migrations.Migrating, Level = LogLevel.Information,
        Message = "Migrating {Vault} from {Current} to {Target}: {Applying} to apply, {Reverting} to revert.")]
    public static partial void Migrating(this ILogger logger,
        string vault,
        string current,
        SemVersion target,
        int applying,
        int reverting);

    [LoggerMessage(EventId = MongoFlowLogEvents.Migrations.MigrationApplied, Level = LogLevel.Information,
        Message = "{Direction} migration {Version} of {Vault}, {Migration}, in {ElapsedMs:0.#} ms.")]
    public static partial void MigrationApplied(this ILogger logger,
        string direction,
        SemVersion version,
        string vault,
        string migration,
        double elapsedMs);

    [LoggerMessage(EventId = MongoFlowLogEvents.Migrations.MigrationFailed, Level = LogLevel.Error,
        Message = "{Direction} migration {Version} of {Vault}, {Migration}, failed after {ElapsedMs:0.#} ms.")]
    public static partial void MigrationFailed(this ILogger logger,
        string direction,
        SemVersion version,
        string vault,
        string migration,
        double elapsedMs,
        Exception exception);

    [LoggerMessage(EventId = MongoFlowLogEvents.Migrations.UpToDate, Level = LogLevel.Debug,
        Message = "{Vault} is at {Version}, its target; there's nothing to migrate.")]
    public static partial void UpToDate(this ILogger logger,
        string vault,
        string version);
}

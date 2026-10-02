namespace MongoFlow;

/// <summary>
/// The names MongoFlow traces and measures with, through <c>System.Diagnostics</c>: its activity source and meter, their
/// spans and instruments, and the tags on them. With OpenTelemetry, add
/// <c>.AddSource(MongoFlowTelemetry.ActivitySourceName)</c> and <c>.AddMeter(MongoFlowTelemetry.MeterName)</c>.
/// </summary>
/// <remarks>
/// MongoFlow traces its own units of work: saves and migrations. The driver traces every command they send, under
/// <c>MongoTelemetry.ActivitySourceName</c>; add that source too to see them nested. Reads have no span of their own:
/// <c>QueryAsync</c>, <c>FindAsync</c> and <c>AggregateAsync</c> return queries that run after they return, and the
/// driver traces them when they do. The meter comes from the <c>IMeterFactory</c> in DI, if there is one.
/// </remarks>
public static class MongoFlowTelemetry
{
    public const string ActivitySourceName = "MongoFlow";

    public const string MeterName = "MongoFlow";

    /// <summary>The spans MongoFlow starts, by operation name.</summary>
    public static class Activities
    {
        /// <summary>
        /// A save that has writes queued: its interceptors, its bulk write and, in a transaction of its own, the commit.
        /// Tagged with <see cref="Tags.Vault"/>, <see cref="Tags.SaveTransaction"/>, <see cref="Tags.OperationCount"/>
        /// and, once written, what it changed.
        /// </summary>
        public const string Save = "MongoFlow.Save";

        /// <summary>
        /// One vault migrated to a target version by <c>IVaultMigrator</c>, tagged with <see cref="Tags.Vault"/>,
        /// <see cref="Tags.MigrationCurrent"/> and <see cref="Tags.MigrationTarget"/>.
        /// </summary>
        public const string Migrate = "MongoFlow.Migrate";

        /// <summary>
        /// One migration applied or reverted, in its transaction unless it opts out, tagged with <see cref="Tags.Vault"/>,
        /// <see cref="Tags.MigrationVersion"/>, <see cref="Tags.MigrationName"/> and <see cref="Tags.MigrationDirection"/>.
        /// </summary>
        public const string Migration = "MongoFlow.Migration";
    }

    /// <summary>The instruments of MongoFlow's meter.</summary>
    public static class Instruments
    {
        /// <summary>
        /// Histogram, in seconds: how long saves that have writes queued take, tagged with <see cref="Tags.Vault"/>,
        /// <see cref="Tags.SaveTransaction"/> and, for a save that failed, <see cref="Tags.ErrorType"/>. A save that joins
        /// an open transaction ends before its commit.
        /// </summary>
        public const string SaveDuration = "mongoflow.save.duration";

        /// <summary>
        /// Counter: the writes saves committed, tagged with <see cref="Tags.Vault"/> and <see cref="Tags.OperationKind"/>.
        /// Writes are counted when their transaction commits, so those rolled back never are.
        /// </summary>
        public const string SaveOperations = "mongoflow.save.operations";

        /// <summary>
        /// Histogram, in seconds: how long transactions stay open, from <c>BeginAsync</c> to their end, tagged with
        /// <see cref="Tags.TransactionOutcome"/> and, when an error ended one, <see cref="Tags.ErrorType"/>. Migrations'
        /// transactions count; the transaction a save opens for itself is part of <see cref="SaveDuration"/>.
        /// </summary>
        public const string TransactionDuration = "mongoflow.transaction.duration";
    }

    /// <summary>The tags on MongoFlow's spans and measurements.</summary>
    public static class Tags
    {
        /// <summary>The vault's type name, such as <c>ShopVault</c>.</summary>
        public const string Vault = "mongoflow.vault";

        /// <summary>
        /// The save's transaction: <c>own</c>, opened and committed by the save, or <c>joined</c>, the scope's open
        /// transaction.
        /// </summary>
        public const string SaveTransaction = "mongoflow.save.transaction";

        /// <summary>The operations a save writes, after its interceptors ran. On spans only.</summary>
        public const string OperationCount = "mongoflow.save.operation_count";

        /// <summary>The documents a save inserted. On spans only.</summary>
        public const string InsertedCount = "mongoflow.save.inserted_count";

        /// <summary>The documents a save's updates and replaces matched. On spans only.</summary>
        public const string MatchedCount = "mongoflow.save.matched_count";

        /// <summary>The documents a save's updates and replaces changed. On spans only.</summary>
        public const string ModifiedCount = "mongoflow.save.modified_count";

        /// <summary>The documents a save deleted. On spans only.</summary>
        public const string DeletedCount = "mongoflow.save.deleted_count";

        /// <summary>A write's kind: <c>insert</c>, <c>replace</c>, <c>update</c> or <c>delete</c>.</summary>
        public const string OperationKind = "mongoflow.operation.kind";

        /// <summary>
        /// How a transaction ended: <c>committed</c>; <c>rolled_back</c>, by <c>RollbackAsync</c>, by disposal without a
        /// commit, or because a save that joined it failed after writing; or <c>commit_failed</c>.
        /// </summary>
        public const string TransactionOutcome = "mongoflow.transaction.outcome";

        /// <summary>The version a vault has before it's migrated, or <c>none</c>.</summary>
        public const string MigrationCurrent = "mongoflow.migration.current";

        /// <summary>The version a vault is migrated to.</summary>
        public const string MigrationTarget = "mongoflow.migration.target";

        /// <summary>A migration's version.</summary>
        public const string MigrationVersion = "mongoflow.migration.version";

        /// <summary>A migration's type name.</summary>
        public const string MigrationName = "mongoflow.migration.name";

        /// <summary>Whether a migration is applied, <c>up</c>, or reverted, <c>down</c>.</summary>
        public const string MigrationDirection = "mongoflow.migration.direction";

        /// <summary>OpenTelemetry's <c>error.type</c>: the full type name of the exception that failed the work.</summary>
        public const string ErrorType = "error.type";
    }
}

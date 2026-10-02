namespace MongoFlow;

/// <summary>
/// The event IDs MongoFlow logs with, and each group's logger category, for filtering logs and setting levels. MongoFlow
/// logs through the <c>ILoggerFactory</c> in DI, if there is one.
/// </summary>
/// <remarks>
/// IDs are <c>27_0gg_eee</c>: <c>gg</c> is the group and <c>eee</c> the event, and 27 is for MongoDB's port, 27017. The
/// range is clear of InsurUp's events (<c>1_0gg_eee</c>), ASP.NET Core's and most libraries' (below 10,000), Npgsql's
/// (1,000 to 6,999), Entity Framework Core's (10,000 to 39,999) and Orleans' (100,000 to 110,000). Every category starts
/// with <c>MongoFlow</c>, so one setting covers them all. Below warnings, everything is <c>Debug</c> or <c>Trace</c>,
/// except an interceptor's failure hook throwing, which is swallowed and so logged as an error, and migrations, which
/// change the database and run rarely, so they log at <c>Information</c>, and a failed one at <c>Error</c>.
/// </remarks>
public static class MongoFlowLogEvents
{
    /// <summary>Building a vault's model, once per vault, the first time it's resolved.</summary>
    public static class Model
    {
        public const string Category = "MongoFlow.Model";

        /// <summary><c>Debug</c>: a vault's model was built, with its database, collections and interceptors.</summary>
        public const int ModelBuilt = 27_001_001;
    }

    /// <summary>
    /// Indexes a collection's key or features rely on that it doesn't have, checked once per vault in the background, only
    /// when warnings are logged. MongoFlow doesn't create indexes.
    /// </summary>
    public static class Indexes
    {
        public const string Category = "MongoFlow.Indexes";

        /// <summary><c>Warning</c>: no unique index covers a key other than <c>_id</c>.</summary>
        public const int KeyIndexMissing = 27_002_001;

        /// <summary><c>Warning</c>: an index covers the key, but none that covers it is unique.</summary>
        public const int KeyIndexNotUnique = 27_002_002;

        /// <summary><c>Warning</c>: a field a feature filters every read on, such as soft delete's flag, is in no index.</summary>
        public const int FieldIndexMissing = 27_002_003;

        /// <summary><c>Debug</c>: a collection has no indexes, so it may not exist yet; it isn't checked.</summary>
        public const int IndexCheckSkipped = 27_002_004;

        /// <summary><c>Debug</c>: a collection's indexes couldn't be listed, such as when the server can't be reached.</summary>
        public const int IndexCheckFailed = 27_002_005;

        /// <summary><c>Debug</c>: every collection of a vault was checked.</summary>
        public const int IndexCheckCompleted = 27_002_006;
    }

    /// <summary>Saves: what they write, how they end, and what fails them.</summary>
    public static class Save
    {
        public const string Category = "MongoFlow.Save";

        /// <summary><c>Debug</c>: a save starts, in a transaction of its own.</summary>
        public const int SavingAlone = 27_003_001;

        /// <summary><c>Debug</c>: a save starts, in the scope's open transaction.</summary>
        public const int SavingInTransaction = 27_003_002;

        /// <summary><c>Trace</c>: one operation of a save is written: its kind, collection and key.</summary>
        public const int Writing = 27_003_003;

        /// <summary><c>Debug</c>: a save ended, with how long it took and what it changed.</summary>
        public const int Saved = 27_003_004;

        /// <summary><c>Debug</c>: a save failed; its exception is thrown to the caller.</summary>
        public const int SaveFailed = 27_003_005;

        /// <summary><c>Error</c>: an interceptor's <c>FailedAsync</c> threw; the exception is swallowed.</summary>
        public const int FailureHookThrew = 27_003_006;

        /// <summary><c>Debug</c>: a write guarded by a concurrency token matched nothing, which fails the save.</summary>
        public const int ConcurrencyConflict = 27_003_007;

        /// <summary><c>Debug</c>: a write carries another tenant than the current one, which fails the save.</summary>
        public const int TenantRejected = 27_003_008;

        /// <summary><c>Debug</c>: the server supports client bulk writes; checked once per client.</summary>
        public const int BulkWritesSupported = 27_003_009;

        /// <summary>
        /// <c>Debug</c>: a save compared the vault's tracked documents with what they were, and found how many changed.
        /// </summary>
        public const int ChangesDetected = 27_003_010;

        /// <summary><c>Error</c>: an interceptor's <c>CommittedAsync</c> threw; the save is committed, so it's swallowed.</summary>
        public const int CommittedHookThrew = 27_003_011;
    }

    /// <summary>
    /// The scope's transactions. One begun with <c>BeginAsync</c> logs at <c>Debug</c>; one a save opens for itself, at
    /// <c>Trace</c>, since the save logs already.
    /// </summary>
    public static class Transaction
    {
        public const string Category = "MongoFlow.Transaction";

        public const int Began = 27_004_001;

        public const int Committed = 27_004_002;

        /// <summary>The commit failed; the saves that joined run their failure hooks, and the exception is thrown.</summary>
        public const int CommitFailed = 27_004_003;

        public const int RolledBack = 27_004_004;

        /// <summary>
        /// A save that joined the transaction failed after writing, so the whole transaction was rolled back; it fails
        /// whatever uses it until it's disposed.
        /// </summary>
        public const int Doomed = 27_004_005;
    }

    /// <summary>Reads, at <c>Trace</c>: the query filters each runs with.</summary>
    public static class Query
    {
        public const string Category = "MongoFlow.Query";

        /// <summary><c>Trace</c>: <c>QueryAsync</c>, <c>FindAsync</c> or <c>AggregateAsync</c>, with its query filters.</summary>
        public const int Reading = 27_005_001;

        /// <summary><c>Trace</c>: <c>GetByKeyAsync</c>, with its key and query filters.</summary>
        public const int ReadingByKey = 27_005_002;
    }

    /// <summary>Migrations <c>IVaultMigrator</c> applies and reverts.</summary>
    public static class Migrations
    {
        public const string Category = "MongoFlow.Migrations";

        /// <summary><c>Information</c>: a vault is migrated from its current version to a target.</summary>
        public const int Migrating = 27_006_001;

        /// <summary><c>Information</c>: a migration was applied or reverted, and recorded.</summary>
        public const int MigrationApplied = 27_006_002;

        /// <summary><c>Error</c>: a migration failed; what it did in its transaction was rolled back.</summary>
        public const int MigrationFailed = 27_006_003;

        /// <summary><c>Debug</c>: a vault is at the target version already.</summary>
        public const int UpToDate = 27_006_004;

        /// <summary>
        /// <c>Warning</c>: a vault's history records a version more than once, as instances migrating at once could under
        /// earlier versions, so its unique index can't be built and doesn't guard against that.
        /// </summary>
        public const int HistoryHasDuplicates = 27_006_005;
    }
}

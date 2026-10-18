namespace MongoFlow;

/// <summary>What the migrator does to a declared index or collection, or, syncing indexes, to one no vault declares.</summary>
internal enum SetupAction
{
    /// <summary>Nothing: it's as declared.</summary>
    Unchanged,

    /// <summary>Creates it: there's none of its name.</summary>
    Create,

    /// <summary>Changes it in place, with <c>collMod</c>.</summary>
    Update,

    /// <summary>Nothing, but fail: it differs in what MongoDB can't change in place.</summary>
    Conflict,

    /// <summary>Syncing indexes: makes a plain index unique in place, with <c>collMod</c>, and anything else it changes.</summary>
    MakeUnique,

    /// <summary>
    /// Syncing indexes: drops the server's index it replaces, of its name, or on the same keys with the same options under
    /// another name, then creates it as declared.
    /// </summary>
    Recreate,

    /// <summary>Syncing indexes: drops one no vault declares.</summary>
    Drop
}

namespace MongoFlow;

/// <summary>What the migrator does to a declared index or collection.</summary>
internal enum SetupAction
{
    /// <summary>Nothing: it's as declared.</summary>
    Unchanged,

    /// <summary>Creates it: there's none of its name.</summary>
    Create,

    /// <summary>Changes it in place, with <c>collMod</c>.</summary>
    Update,

    /// <summary>Nothing, but fail: it differs in what MongoDB can't change in place.</summary>
    Conflict
}

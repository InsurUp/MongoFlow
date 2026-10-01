namespace MongoFlow;

/// <summary>
/// Where a setting came from. The vault's own configuration runs first so its skips are known before defaults apply,
/// but defaults still count as coming first: an own value wins, and default list items come before own ones.
/// </summary>
internal enum Layer
{
    Default,
    Own
}

using MongoDB.Bson;

namespace MongoFlow;

/// <summary>The element paths that differ between two serializations of a document, as <see cref="BsonDiff"/> found them.</summary>
internal sealed class BsonChanges
{
    private readonly List<(string Path, bool Removed)> _paths = [];

    public int Count => _paths.Count;

    /// <summary>
    /// Whether a top-level element that changed has a name an update can't address, such as <c>a.b</c> or <c>$a</c>, so the
    /// document has to be replaced.
    /// </summary>
    public bool NeedsReplace { get; set; }

    public void Set(string path) => _paths.Add((path, false));

    public void Unset(string path) => _paths.Add((path, true));

    /// <summary>Drops the paths found after the first <paramref name="count"/>.</summary>
    public void Truncate(int count) => _paths.RemoveRange(count, _paths.Count - count);

    /// <summary>
    /// Whether a change reaches one of <paramref name="fields"/>: the field itself, a field inside it, or a document that
    /// holds it.
    /// </summary>
    public bool Touches(IReadOnlyList<string> fields)
    {
        foreach (var (path, _) in _paths)
        {
            foreach (var field in fields)
            {
                if (path == field || IsInside(path, field) || IsInside(field, path))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// <c>{ $set: { path: value }, $unset: { path: "" } }</c>, with each value read from <paramref name="current"/>, the
    /// document as it is now.
    /// </summary>
    public BsonDocument ToUpdate(BsonDocument current)
    {
        BsonDocument? set = null;
        BsonDocument? unset = null;

        foreach (var (path, removed) in _paths)
        {
            if (removed)
            {
                (unset ??= new BsonDocument()).Add(path, "");
            }
            else
            {
                (set ??= new BsonDocument()).Add(path, ValueAt(current, path));
            }
        }

        var update = new BsonDocument();
        if (set is not null)
        {
            update.Add("$set", set);
        }

        if (unset is not null)
        {
            update.Add("$unset", unset);
        }

        return update;
    }

    private static bool IsInside(string path,
        string document) =>
        path.Length > document.Length && path[document.Length] == '.' && path.StartsWith(document, StringComparison.Ordinal);

    // The paths' names hold no dots: a name with one is set with the document that holds it.
    private static BsonValue ValueAt(BsonDocument document,
        string path)
    {
        BsonValue value = document;
        foreach (var name in path.Split('.'))
        {
            value = value.AsBsonDocument[name];
        }

        return value;
    }
}

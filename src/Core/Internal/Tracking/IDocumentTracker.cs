namespace MongoFlow;

/// <summary>
/// Tracks the documents a read returns, for the wrappers around the driver's queries and cursors, and those a save writes
/// whole, for the inserts and replaces queued through a view that tracks changes.
/// </summary>
internal interface IDocumentTracker<in TDocument>
{
    void Track(TDocument document);

    /// <summary>
    /// <paramref name="document"/>, with its BSON as it is now, ready to be tracked; <see langword="null"/> when it has no
    /// key, since one without a key can't be updated by key.
    /// </summary>
    TrackedDocument? Snapshot(TDocument document);
}

namespace MongoFlow;

/// <summary>A document a read returned, with what it looked like then, for saves to compare it against.</summary>
internal abstract class TrackedDocument(object document,
    BsonSnapshot snapshot)
{
    public object Document { get; } = document;

    /// <summary>What saves compare the document against: as read, or as the last save wrote it.</summary>
    public BsonSnapshot Snapshot { get; set; } = snapshot;

    /// <summary>A save deleted it: it's no longer compared, and it's forgotten once that save commits.</summary>
    public bool Deleting { get; set; }

    /// <summary>Whether its snapshot was given back, by the vault's disposal or the commit of its delete.</summary>
    public bool Released { get; private set; }

    public void Release()
    {
        Released = true;
        Snapshot.Return();
    }

    /// <summary>The document as it is now.</summary>
    public abstract BsonSnapshot Serialize();

    /// <summary>Whether the document differs from <see cref="Snapshot"/>.</summary>
    public bool HasChanged()
    {
        var current = Serialize();
        try
        {
            return BsonDiff.Compare(Snapshot.Span, current.Span) is not null;
        }
        finally
        {
            current.Return();
        }
    }

    /// <summary>The write that brings the stored document up to date, or <see langword="null"/> when it hasn't changed.</summary>
    /// <exception cref="InvalidOperationException">Its key changed.</exception>
    public abstract VaultOperation? DetectChange();
}

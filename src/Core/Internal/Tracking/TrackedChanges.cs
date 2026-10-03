using System.Runtime.InteropServices;
using MongoDB.Bson;

namespace MongoFlow;

/// <summary>
/// What one save does to tracked documents: writes their changes or replaces them, which refreshes their snapshots, or
/// deletes them. Once its writes are in, later saves compare against what it wrote; rolled back, against what was there
/// before. It keeps each document's snapshot from before the save, the original its operations expose, alive until its
/// interceptors' hooks have run, and gives back what it holds once, when it ends.
/// </summary>
internal sealed class TrackedChanges(ChangeTracker tracker)
{
    // Original: the document's snapshot when the save began. Held: whether the save holds it, after swapping it out or
    // forgetting the document it deleted, rather than the document. Copy: the original as an interceptor first read it.
    private readonly List<(TrackedDocument Document,
        bool Delete,
        BsonSnapshot Original,
        bool Held,
        RawBsonDocument? Copy)> _changes = [];
    private bool _applied;
    private bool _ended;

    public bool IsEmpty => _changes.Count == 0;

    /// <summary>Records that the save writes or replaces <paramref name="document"/>; returns its index.</summary>
    public int Refresh(TrackedDocument document) => Add(document, delete: false);

    /// <summary>Records that the save deletes <paramref name="document"/>; returns its index.</summary>
    public int Delete(TrackedDocument document) => Add(document, delete: true);

    /// <summary>
    /// Takes new snapshots of the documents written, after the save's writes and its interceptors' hooks before the commit,
    /// so they hold what interceptors changed too, such as an incremented concurrency token. Deleted documents stop being
    /// compared.
    /// </summary>
    public void Apply()
    {
        lock (tracker.Lock)
        {
            foreach (ref var change in CollectionsMarshal.AsSpan(_changes))
            {
                // The vault was disposed during the save.
                if (change.Document.Released)
                {
                    continue;
                }

                if (change.Delete)
                {
                    change.Document.Deleting = true;
                }
                else
                {
                    change.Held = true;
                    change.Document.Snapshot = change.Document.Serialize();
                }
            }

            _applied = true;
        }
    }

    /// <summary>Forgets the documents deleted, holding their snapshots until the save ends.</summary>
    public void Commit()
    {
        lock (tracker.Lock)
        {
            foreach (ref var change in CollectionsMarshal.AsSpan(_changes))
            {
                if (change.Delete && !change.Document.Released)
                {
                    tracker.Forget(change.Document);
                    change.Held = true;
                }
            }
        }
    }

    /// <summary>Puts back the snapshots swapped out, so the changes are pending again.</summary>
    public void Revert()
    {
        if (!_applied)
        {
            return;
        }

        lock (tracker.Lock)
        {
            foreach (ref var change in CollectionsMarshal.AsSpan(_changes))
            {
                if (change.Delete)
                {
                    change.Document.Deleting = false;
                }
                else if (change.Held && !change.Document.Released)
                {
                    change.Document.Snapshot.Return();
                    change.Document.Snapshot = change.Original;
                    change.Held = false;
                }
            }

            // So another rollback of the save finds nothing to undo.
            _applied = false;
        }
    }

    /// <summary>
    /// The save's interceptors' hooks have run: gives back the snapshots it holds. Originals nobody read can't be read
    /// any more, as their snapshots may be given back.
    /// </summary>
    public void End()
    {
        lock (tracker.Lock)
        {
            _ended = true;

            foreach (ref var change in CollectionsMarshal.AsSpan(_changes))
            {
                if (change.Held)
                {
                    change.Original.Return();
                    change.Held = false;
                }
            }
        }
    }

    /// <summary>The document the change at <paramref name="index"/> was made to, as it was before the save.</summary>
    /// <exception cref="InvalidOperationException">
    /// The save ended, or the vault was disposed, before it was first read.
    /// </exception>
    public RawBsonDocument ReadOriginal(int index)
    {
        lock (tracker.Lock)
        {
            ref var change = ref CollectionsMarshal.AsSpan(_changes)[index];
            if (change.Copy is null)
            {
                // The snapshot is still there while the save holds it, or the document does.
                if (_ended || (!change.Held && change.Document.Released))
                {
                    throw new InvalidOperationException(
                        "A document's original is read while its save's interceptors run, before the vault is " +
                        "disposed. Read it there, or keep what it returned.");
                }

                change.Copy = new RawBsonDocument(change.Original.Span.ToArray());
            }

            return change.Copy;
        }
    }

    private int Add(TrackedDocument document,
        bool delete)
    {
        _changes.Add((document, delete, document.Snapshot, false, null));

        return _changes.Count - 1;
    }
}

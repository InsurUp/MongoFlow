using System.Runtime.InteropServices;

namespace MongoFlow;

/// <summary>
/// What one save does to tracked documents: writes their changes or replaces them, which refreshes their snapshots, or
/// deletes them. Once its writes are in, later saves compare against what it wrote; rolled back, against what was there
/// before. Each snapshot it swaps out is its own to give back.
/// </summary>
internal sealed class TrackedChanges(ChangeTracker tracker)
{
    private readonly List<(TrackedDocument Document, bool Delete, BsonSnapshot Previous)> _changes = [];
    private bool _applied;

    public bool IsEmpty => _changes.Count == 0;

    public void Refresh(TrackedDocument document) => _changes.Add((document, false, default));

    public void Delete(TrackedDocument document) => _changes.Add((document, true, default));

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
                    change.Previous = change.Document.Snapshot;
                    change.Document.Snapshot = change.Document.Serialize();
                }
            }

            _applied = true;
        }
    }

    /// <summary>Gives back the snapshots swapped out, and forgets the documents deleted.</summary>
    public void Commit()
    {
        lock (tracker.Lock)
        {
            foreach (var change in _changes)
            {
                if (!change.Delete)
                {
                    change.Previous.Return();
                }
                else if (!change.Document.Released)
                {
                    tracker.Forget(change.Document);
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
            foreach (var change in _changes)
            {
                if (change.Delete)
                {
                    change.Document.Deleting = false;
                }
                else if (change.Document.Released)
                {
                    change.Previous.Return();
                }
                else
                {
                    change.Document.Snapshot.Return();
                    change.Document.Snapshot = change.Previous;
                }
            }
        }
    }
}

using System.Runtime.InteropServices;
using Prest;

namespace MongoFlow;

/// <summary>
/// The documents a vault instance's reads tracked, in the order they were read. Reads may track from parallel tasks, and
/// the vault's disposal may come while a save is under way, so everything that touches a snapshot holds <see cref="Lock"/>.
/// </summary>
internal sealed class ChangeTracker : IDisposable
{
    private readonly OrderedDictionary<object, TrackedDocument> _documents = new(ReferenceEqualityComparer.Instance);

    public Lock Lock { get; } = new();

    public void Track(TrackedDocument document)
    {
        lock (Lock)
        {
            // A read returns new instances, so each is tracked once.
            _documents[document.Document] = document;
        }
    }

    /// <summary>
    /// Puts an update for each tracked document that changed before <paramref name="operations"/>, the writes queued on
    /// the vault, so a document changed and then deleted by a queued write is updated first. A queued replace or delete of
    /// a tracked document takes the place of its changes. Each of these writes carries the document as it was before
    /// the save.
    /// </summary>
    /// <returns>What the save does to tracked documents, or <see langword="null"/> when it does nothing to them.</returns>
    /// <exception cref="InvalidOperationException">A tracked document's key changed.</exception>
    public TrackedChanges? DetectChanges(ref PooledList<VaultOperation>? operations,
        VaultModel model)
    {
        lock (Lock)
        {
            if (_documents.Count == 0)
            {
                return null;
            }

            var changes = new TrackedChanges(this);
            var replacedOrDeleted = ReplacedOrDeleted(operations, changes);
            List<VaultOperation>? updates = null;

            foreach (var document in _documents.Values)
            {
                if (document.Deleting || replacedOrDeleted?.ContainsKey(document) == true ||
                    document.DetectChange() is not { } update)
                {
                    continue;
                }

                update.TrackedOriginal = new TrackedOriginal(changes, changes.Refresh(document));
                (updates ??= []).Add(update);
            }

            model.Logs.Save.ChangesDetected(model.VaultType.Name, updates?.Count ?? 0, _documents.Count);

            if (updates is not null)
            {
                var merged = new PooledList<VaultOperation>(updates.Count + (operations?.Count ?? 0), clearOnReturn: true);
                merged.AddRange(CollectionsMarshal.AsSpan(updates));

                if (operations is not null)
                {
                    merged.AddRange(operations.Span);
                    operations.Dispose();
                }

                operations = merged;
            }

            return changes.IsEmpty ? null : changes;
        }
    }

    /// <summary>Whether a tracked document changed since it was read or last saved.</summary>
    public bool HasChanges()
    {
        lock (Lock)
        {
            foreach (var document in _documents.Values)
            {
                if (!document.Deleting && document.HasChanged())
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Forgets a document whose delete committed; the save that deleted it gives its snapshot back. Called holding
    /// <see cref="Lock"/>.
    /// </summary>
    public void Forget(TrackedDocument document) => _documents.Remove(document.Document);

    /// <summary>Gives every snapshot back. The tracker starts again empty.</summary>
    public void Dispose()
    {
        lock (Lock)
        {
            foreach (var document in _documents.Values)
            {
                document.Release();
            }

            _documents.Clear();
        }
    }

    /// <summary>
    /// The tracked documents a queued write replaces or deletes, recorded in <paramref name="changes"/>: deleted if any
    /// write deletes them. Each of those writes carries the document as it was before the save.
    /// </summary>
    private Dictionary<TrackedDocument, int>? ReplacedOrDeleted(PooledList<VaultOperation>? operations,
        TrackedChanges changes)
    {
        if (operations is null)
        {
            return null;
        }

        List<(VaultOperation Operation, TrackedDocument Document)>? writes = null;
        foreach (var operation in operations)
        {
            if (operation is { Kind: OperationKind.Replace or OperationKind.Delete, Document: { } document } &&
                _documents.TryGetValue(document, out var tracked))
            {
                (writes ??= []).Add((operation, tracked));
            }
        }

        if (writes is null)
        {
            return null;
        }

        Dictionary<TrackedDocument, bool> deleted = [];
        foreach (var (operation, document) in writes)
        {
            deleted[document] = deleted.GetValueOrDefault(document) | operation.Kind == OperationKind.Delete;
        }

        var indexes = new Dictionary<TrackedDocument, int>(deleted.Count);
        foreach (var (document, delete) in deleted)
        {
            indexes[document] = delete ? changes.Delete(document) : changes.Refresh(document);
        }

        foreach (var (operation, document) in writes)
        {
            operation.TrackedOriginal = new TrackedOriginal(changes, indexes[document]);
        }

        return indexes;
    }
}

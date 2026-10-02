using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A transaction vaults join. Its session starts with the first vault that joins, so it runs on that vault's client.
/// </summary>
/// <remarks>
/// A save that fails after writing dooms it: MongoDB can't undo part of a transaction, so the whole of it is rolled back
/// at once. It stays current, failing whatever tries to use it, its commit included, until its owner ends it.
/// </remarks>
/// <param name="forSave">
/// Whether a save opens it for itself: it logs at a lower level than a begun one, since the save logs already, and its
/// duration is the save's.
/// </param>
internal sealed class VaultTransaction(VaultTransactionManager owner,
    IMongoClient? defaultClient,
    bool forSave) : IVaultTransaction
{
    private readonly LogLevel _logLevel = forSave ? LogLevel.Trace : LogLevel.Debug;
    private readonly long _started = Stopwatch.GetTimestamp();

    // The span current where the transaction began, for the driver's transaction span to start under.
    private readonly Activity? _begunIn = Activity.Current;

    private readonly List<SaveCallbacks> _saves = [];
    private IMongoClient? _client;
    private IClientSessionHandle? _session;
    private bool _ended;
    private Exception? _doomedBy;

    public bool IsCommitted { get; private set; }

    public IClientSessionHandle Session
    {
        get
        {
            ThrowIfUnusable();

            if (_session is not null)
            {
                return _session;
            }

            var client = defaultClient ?? throw new InvalidOperationException(
                "No vault has joined the transaction yet, and no IMongoClient is registered to start it with.");

            return Start(client, client.StartSession());
        }
    }

    /// <summary>The transaction's session, started on <paramref name="client"/> by the first vault that asks for it.</summary>
    public async ValueTask<IClientSessionHandle> GetSessionAsync(IMongoClient client,
        CancellationToken cancellationToken)
    {
        ThrowIfUnusable();

        if (_session is null)
        {
            return Start(client, await client.StartSessionAsync(cancellationToken: cancellationToken));
        }

        if (!ReferenceEquals(_client, client))
        {
            throw new InvalidOperationException("A vault on a different client can't join this transaction.");
        }

        return _session;
    }

    public void Enlist(SaveCallbacks save) => _saves.Add(save);

    public void Unenlist(SaveCallbacks save) => _saves.Remove(save);

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfEnded();
        End();

        if (_doomedBy is not null)
        {
            throw Doomed();
        }

        try
        {
            if (_session is { IsInTransaction: true })
            {
                await _session.CommitTransactionAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            owner.Log.CommitFailed(_logLevel, _saves.Count, exception);
            Record("commit_failed", exception);
            await FailAsync(exception);
            throw;
        }

        IsCommitted = true;
        owner.Log.Committed(_logLevel, _saves.Count);
        Record("committed", null);

        foreach (var save in _saves)
        {
            await save.Committed(cancellationToken);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_ended)
        {
            return;
        }

        End();

        // A doomed transaction was rolled back already, and its saves' failure hooks have run.
        if (_doomedBy is not null)
        {
            return;
        }

        if (_session is { IsInTransaction: true })
        {
            await _session.AbortTransactionAsync(CancellationToken.None);
        }

        owner.Log.RolledBack(_logLevel, _saves.Count);
        Record("rolled_back", null);
        await FailAsync(new InvalidOperationException("The transaction was rolled back."));
    }

    /// <summary>
    /// Rolls the transaction back because a save that joined it failed after writing, running every joined save's
    /// failure hooks. It stays current, so whatever tries to use it next fails, instead of running outside it.
    /// </summary>
    public async Task DoomAsync(Exception cause)
    {
        _doomedBy = cause;

        // A save that wrote joined first, so there's a session. The server aborts on a write it rejects already.
        if (_session!.IsInTransaction)
        {
            await _session.AbortTransactionAsync(CancellationToken.None);
        }

        owner.Log.Doomed(_logLevel, _saves.Count, cause);
        Record("rolled_back", cause);
        await FailAsync(cause);
    }

    public async ValueTask DisposeAsync()
    {
        await RollbackAsync();
        _session?.Dispose();

        // Every hook a joined save can run has run, so the saves give back what they rented.
        foreach (var save in _saves)
        {
            save.Release();
        }

        _saves.Clear();
    }

    private IClientSessionHandle Start(IMongoClient client, IClientSessionHandle session)
    {
        _client = client;
        _session = session;

        // The driver starts its transaction span here, under the current one: make that where the transaction began,
        // rather than whichever vault happened to join first.
        var current = Activity.Current;
        Activity.Current = _begunIn;
        try
        {
            _session.StartTransaction();
        }
        finally
        {
            Activity.Current = current;
        }

        return _session;
    }

    /// <summary>Records how long the transaction was open, once it ended; a save's own transaction is part of the save's.</summary>
    private void Record(string outcome,
        Exception? exception)
    {
        if (!forSave)
        {
            owner.Metrics.RecordTransaction(Stopwatch.GetElapsedTime(_started), outcome, exception);
        }
    }

    private void End()
    {
        _ended = true;
        owner.End(this);
    }

    // Newest save first, so a document two saves changed is put back to what the first one read.
    private async Task FailAsync(Exception exception)
    {
        for (var i = _saves.Count - 1; i >= 0; i--)
        {
            await _saves[i].Failed(exception, CancellationToken.None);
        }
    }

    private void ThrowIfEnded()
    {
        if (_ended)
        {
            throw new InvalidOperationException("The transaction has already ended.");
        }
    }

    private void ThrowIfUnusable()
    {
        ThrowIfEnded();

        if (_doomedBy is not null)
        {
            throw Doomed();
        }
    }

    private InvalidOperationException Doomed() =>
        new("The transaction was rolled back: a save that joined it failed after writing, and MongoDB can't undo part " +
            "of a transaction. Dispose of it, and start another.", _doomedBy);
}

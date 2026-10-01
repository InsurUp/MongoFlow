using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A transaction vaults join. Its session starts with the first vault that joins, so it runs on that vault's client.
/// </summary>
internal sealed class VaultTransaction(VaultTransactionManager owner,
    IMongoClient? defaultClient,
    LogLevel logLevel) : IVaultTransaction
{
    private readonly List<SaveCallbacks> _saves = [];
    private IMongoClient? _client;
    private IClientSessionHandle? _session;
    private bool _ended;

    public bool IsCommitted { get; private set; }

    public IClientSessionHandle Session
    {
        get
        {
            ThrowIfEnded();

            if (_session is not null)
            {
                return _session;
            }

            var client = defaultClient ?? throw new InvalidOperationException(
                "No vault has joined the transaction yet, and no IMongoClient is registered to start it with.");

            return Start(client, client.StartSession());
        }
    }

    public async ValueTask<IClientSessionHandle> JoinAsync(IMongoClient client, CancellationToken cancellationToken)
    {
        ThrowIfEnded();

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

        try
        {
            if (_session is { IsInTransaction: true })
            {
                await _session.CommitTransactionAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            owner.Log.CommitFailed(logLevel, _saves.Count, exception);
            await FailAsync(exception);
            throw;
        }

        IsCommitted = true;
        owner.Log.Committed(logLevel, _saves.Count);

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

        if (_session is { IsInTransaction: true })
        {
            await _session.AbortTransactionAsync(CancellationToken.None);
        }

        owner.Log.RolledBack(logLevel, _saves.Count);
        await FailAsync(new InvalidOperationException("The transaction was rolled back."));
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
        _session.StartTransaction();

        return _session;
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
}

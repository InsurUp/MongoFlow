using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed class VaultTransactions(IServiceProvider services) : IVaultTransactions
{
    public IVaultTransaction? Current => Active;

    public VaultTransaction? Active { get; private set; }

    public Task<IVaultTransaction> BeginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IVaultTransaction>(Start());

    public VaultTransaction Start()
    {
        if (Active is not null)
        {
            throw new InvalidOperationException("A transaction is already open in this scope.");
        }

        return Active = new VaultTransaction(this, services.GetService<IMongoClient>());
    }

    public void End(VaultTransaction transaction)
    {
        if (ReferenceEquals(Active, transaction))
        {
            Active = null;
        }
    }
}

/// <summary>
/// A transaction vaults join. Its session starts with the first vault that joins, so it runs on that vault's client.
/// </summary>
internal sealed class VaultTransaction(VaultTransactions owner, IMongoClient? defaultClient) : IVaultTransaction
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
            await FailAsync(exception);
            throw;
        }

        IsCommitted = true;

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

        await FailAsync(new InvalidOperationException("The transaction was rolled back."));
    }

    public async ValueTask DisposeAsync()
    {
        await RollbackAsync();
        _session?.Dispose();
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

    private async Task FailAsync(Exception exception)
    {
        foreach (var save in _saves)
        {
            await save.Failed(exception, CancellationToken.None);
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

/// <summary>What to run for a save when the transaction it joined commits or fails.</summary>
internal sealed record SaveCallbacks(
    Func<CancellationToken, Task> Committed,
    Func<Exception, CancellationToken, Task> Failed);

# Transactions

Every save runs in a transaction. With none open in the scope, a save starts its own and commits it when its writes are
in; its interceptors see it while their hooks run, so saves they make, such as to an audit vault, join it.

## Several saves in one transaction

`IVaultTransactionManager`, scoped, begins a transaction that every vault saved in the scope joins until it ends:

```csharp
public sealed class ClaimService(IPolicyVault policies,
    CustomerVault customers,
    IVaultTransactionManager transactions)
{
    public async Task FileAsync(Claim claim, CancellationToken cancellationToken)
    {
        await using var transaction = await transactions.BeginAsync(cancellationToken);

        var policy = await policies.Policies.GetByKeyAsync(claim.PolicyNumber, cancellationToken)
            ?? throw new KeyNotFoundException(claim.PolicyNumber);

        policies.Claims.Add(claim);
        policies.Policies.UpdateByKey(policy.PolicyNumber, Builders<Policy>.Update.Set(p => p.HasOpenClaim, true));
        await policies.SaveAsync(cancellationToken);

        customers.Customers.UpdateByKey(policy.CustomerId, Builders<Customer>.Update.Inc(c => c.OpenClaims, 1));
        await customers.SaveAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
```

- `CommitAsync` commits every save that joined. `RollbackAsync` undoes them, and so does disposing a transaction that
  wasn't committed.
- Reads made while it's open run in its session, so they see its writes.
- Its session starts with the first vault that joins, so every vault saved in it must use the same client; one on
  another client fails with `InvalidOperationException`.
- `Current` is the open transaction, if there is one. One scope has one open transaction at a time.
- MongoDB runs a transaction's operations one at a time: inside it, run reads and saves one after another, not from
  parallel tasks.

### Rolling back on a rule

A rule checked after a save can still undo it:

```csharp
policies.Claims.Add(claim);
await policies.SaveAsync(cancellationToken);

var customer = await customers.Customers.GetByKeyAsync(customerId, cancellationToken);
if (customer!.OpenClaims >= MaxOpenClaims)
{
    await transaction.RollbackAsync(cancellationToken); // the claim saved above is undone
    return false;
}
```

## When a save fails

A save that fails before writing, such as an interceptor rejecting it in `SavingAsync`, leaves the transaction as it was:
the other saves in it can still commit.

A save that fails after writing dooms the transaction. MongoDB can't undo part of a transaction, so the whole of it is
rolled back at once, every save that joined it included. It stays current, so whatever tries to use it fails, its commit
included, with `InvalidOperationException` holding the save's failure as its inner exception. Dispose of it, and start
another.

## What interceptors see

- `SavedAsync` runs right after each save's write, before the commit: its writes are in the transaction, but not
  committed. A transactional outbox writes there, with `context.Session`.
- `CommittedAsync` runs once the transaction commits, for every save that joined it, in save order.
- `FailedAsync` runs when the transaction rolls back, for every save that joined it, newest save first.

See [interceptors](interceptors.md).

## Observability

A begun transaction logs at `Debug` under `MongoFlow.Transaction`, and one a save opens for itself at `Trace`, since the
save logs already. The `mongoflow.transaction.duration` histogram measures how long begun transactions stay open, by
outcome: committed, rolled back, or with a failed commit. See [observability](observability.md).

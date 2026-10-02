using MongoDB.Driver;
using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Interceptors;

/// <summary>
/// A decided claim stays decided: an update of one claim applies only while the stored claim is still open, and a save
/// whose update found it decided fails. The server checks the condition in the write itself, so of two requests deciding
/// the same claim at once, only one wins.
/// </summary>
public sealed class ClaimDecisionGuard : VaultInterceptor
{
    private static readonly FilterDefinition<Claim> StillOpen = Builders<Claim>.Filter.Eq(claim => claim.Status, ClaimStatus.Open);

    public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var operation in context.Operations)
        {
            if (operation is UpdateOperation<Claim> { IsSetBased: false } update)
            {
                update.AddCondition(StillOpen);
            }
        }

        return ValueTask.CompletedTask;
    }

    // A write whose condition fails matches nothing, and the save goes on: what that means is this guard's to decide.
    public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
    {
        foreach (var operation in context.Operations)
        {
            if (operation is UpdateOperation<Claim> { IsSetBased: false, Result.Matched: 0 } update)
            {
                throw new ClaimAlreadyDecidedException((Guid)update.Key!);
            }
        }

        return ValueTask.CompletedTask;
    }
}

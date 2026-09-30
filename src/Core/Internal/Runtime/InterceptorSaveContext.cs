using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A save as one interceptor sees it: the shared list, minus operations it doesn't see.</summary>
internal sealed class InterceptorSaveContext(SaveRun run, InterceptorModel interceptor) : SaveContext
{
    public override IMongoVault Vault => run.Runtime.Vault;

    public override IServiceProvider Services => run.Runtime.Services;

    public override IClientSessionHandle Session => run.Session;

    public override IReadOnlyList<VaultOperation> Operations =>
        run.Operations.Where(operation => run.Sees(interceptor, operation)).ToList();

    public override SaveResult? Result => run.Result;

    public override IDictionary<object, object?> Items => run.Items;

    public override void Replace(VaultOperation operation, VaultOperation replacement) => run.Replace(operation, replacement);

    public override void Remove(VaultOperation operation) => run.Remove(operation);
}

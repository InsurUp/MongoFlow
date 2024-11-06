using MongoDB.Driver;

namespace MongoFlow;

internal readonly struct VaultOperationContext
{
    public VaultOperationContext(IClientSessionHandle session,
        MongoVault vault,
        bool enableDiagnostic)
    {
        Session = session;
        Vault = vault;
        EnableDiagnostic = enableDiagnostic;
    }


    public IClientSessionHandle Session { get; }

    public MongoVault Vault { get; }
    public bool EnableDiagnostic { get; }
}

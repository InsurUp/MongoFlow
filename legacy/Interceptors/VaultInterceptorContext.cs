using MongoDB.Driver;

namespace MongoFlow;

public record VaultInterceptorContext(
    MongoVault Vault,
    List<VaultOperation> Operations,
    IClientSessionHandle Session,
    IServiceProvider ServiceProvider)
{
    private bool _diagnosticsEnabled;

    public bool DiagnosticsEnabled => _diagnosticsEnabled;

    public void EnableDiagnostics()
    {
        _diagnosticsEnabled = true;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MongoFlow;

internal sealed class MongoMigrationStartupRunner<TVault> : IHostedService where TVault : MongoVault
{
    private readonly IServiceProvider _serviceProvider;

    public MongoMigrationStartupRunner(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        
        var vault = scope.ServiceProvider.GetRequiredService<TVault>();

        var result = await vault.MigrationManager.MigrateAsync(cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new MongoMigrationFailedException(result);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
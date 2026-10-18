using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using TUnit.Core.Interfaces;

namespace MongoFlow.IntegrationTests;

/// <summary>
/// A throwaway MongoDB with MongoDB Search, shared by every search test in the run: one of each kind the search tests run
/// on, <see cref="AtlasLocalFixture"/> and <see cref="CommunitySearchFixture"/>, which list an index's definition each
/// their own way. Tests stay isolated by each using a database of its own.
/// </summary>
public abstract class SearchFixture : IAsyncInitializer, IAsyncDisposable
{
    private MongoClient? _client;

    /// <summary>The client every test shares.</summary>
    public IMongoClient Client => _client ?? throw new InvalidOperationException("The fixture hasn't started.");

    public async Task InitializeAsync() => _client = new MongoClient(await StartAsync());

    /// <summary>A database no other test uses.</summary>
    public IMongoDatabase NewDatabase() => Client.GetDatabase($"t{Guid.NewGuid():N}");

    /// <summary>A <typeparamref name="TVault"/> on a new database of the shared client.</summary>
    public VaultHost<TVault> Host<TVault>(Action<IVaultBuilder<TVault>>? configure = null,
        Action<IServiceCollection>? services = null) where TVault : MongoVault =>
        new(Client, NewDatabase(), configure, services);

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await StopAsync();
    }

    /// <summary>Starts the server, and gives its connection string once it serves search commands.</summary>
    protected abstract Task<string> StartAsync();

    /// <summary>Stops the server and removes it.</summary>
    protected abstract ValueTask StopAsync();
}

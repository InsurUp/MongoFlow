using Microsoft.Extensions.DependencyInjection;
using MongoFlow.Samples.Infrastructure;

namespace MongoFlow.Samples.Walkthrough;

/// <summary>Runs code as a request would: in a DI scope of its own, with its own vault instances, signed in as someone.</summary>
public sealed class SampleRequests(IServiceProvider services)
{
    public async Task<T> AsAsync<T>(SampleUser user, Func<IServiceProvider, Task<T>> request)
    {
        await using var scope = Begin(user);

        return await request(scope.ServiceProvider);
    }

    public async Task AsAsync(SampleUser user, Func<IServiceProvider, Task> request)
    {
        await using var scope = Begin(user);
        await request(scope.ServiceProvider);
    }

    /// <summary>A request left open, for a step that interleaves two of them.</summary>
    public AsyncServiceScope Begin(SampleUser user)
    {
        var scope = services.CreateAsyncScope();
        user.SignIn(scope.ServiceProvider.GetRequiredService<RequestUser>());

        return scope;
    }
}

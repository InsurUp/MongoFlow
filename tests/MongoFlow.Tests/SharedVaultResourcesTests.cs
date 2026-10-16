namespace MongoFlow.Tests;

/// <summary>
/// <see cref="SharedVaultResources"/> keeps what the vaults of one root provider share:
/// <list type="number">
/// <item>a resource is created once per key, the first time it's asked for;</item>
/// <item>disposing disposes every disposable resource, once, and nothing is handed out after.</item>
/// </list>
/// </summary>
public class SharedVaultResourcesTests
{
    [Test]
    public async Task GetOrAdd_SameKey_CreatesTheResourceOnce()
    {
        // Arrange
        using var shared = new SharedVaultResources();
        var created = 0;

        // Act
        var first = shared.GetOrAdd("key", () => new Counted(++created));
        var second = shared.GetOrAdd("key", () => new Counted(++created));
        var other = shared.GetOrAdd("other", () => new Counted(++created));

        // Assert
        await Verify(new { First = first.Number, Second = second.Number, Other = other.Number });
    }

    [Test]
    public async Task Dispose_HeldResources_DisposesThemOnceAndHandsOutNothingMore()
    {
        // Arrange
        var shared = new SharedVaultResources();
        var resource = shared.GetOrAdd("key", () => new Counted(1));
        shared.GetOrAdd("plain", () => new object());

        // Act
        shared.Dispose();
        shared.Dispose();

        // Assert
        await Assert.That(resource.Disposals).IsEqualTo(1);
        await Assert.That(() => shared.GetOrAdd("key", () => new Counted(2))).ThrowsExactly<ObjectDisposedException>();
    }

    /// <summary>A resource that counts how often it's disposed.</summary>
    private sealed class Counted(int number) : IDisposable
    {
        public int Number { get; } = number;

        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }
}

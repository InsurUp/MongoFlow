using TUnit.Assertions.Enums;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="OwnedInterceptors"/> disposes the interceptors MongoFlow created for a scope, with the scope: the disposable
/// ones only, newest first, asynchronously when they can be, and once. Disposed synchronously, it fails for one that can
/// only be disposed asynchronously, as DI does, after disposing the others.
/// </summary>
public partial class OwnedInterceptorsTests
{
    private readonly List<string> _log = [];
    private readonly OwnedInterceptors _owned = new();

    [Test]
    public async Task DisposeAsync_TrackedInterceptors_DisposesTheDisposableOnesNewestFirst()
    {
        // Arrange
        _owned.Track(new Disposable("first", _log));
        _owned.Track(new Plain());
        _owned.Track(new AsyncDisposable("second", _log));
        _owned.Track(new BothDisposable("third", _log));

        // Act
        await _owned.DisposeAsync();

        // Assert
        await Assert.That(_log).IsEquivalentTo(["third disposed asynchronously", "second disposed asynchronously", "first disposed"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task DisposeAsync_Twice_DisposesOnce()
    {
        // Arrange
        _owned.Track(new Disposable("only", _log));
        await _owned.DisposeAsync();

        // Act
        await _owned.DisposeAsync();

        // Assert
        await Assert.That(_log).IsEquivalentTo(["only disposed"]);
    }

    [Test]
    public async Task Dispose_SynchronouslyDisposableInterceptors_DisposesThemNewestFirst()
    {
        // Arrange
        _owned.Track(new Disposable("first", _log));
        _owned.Track(new BothDisposable("second", _log));

        // Act
        _owned.Dispose();

        // Assert
        await Assert.That(_log).IsEquivalentTo(["second disposed", "first disposed"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Dispose_InterceptorThatOnlyDisposesAsynchronously_DisposesTheOthersAndThrows()
    {
        // Arrange
        _owned.Track(new Disposable("first", _log));
        _owned.Track(new AsyncDisposable("second", _log));

        // Act
        var exception = await Assert.That(_owned.Dispose).ThrowsExactly<InvalidOperationException>();

        // Assert
        await Verify(new { exception!.Message, Log = _log });
    }
}

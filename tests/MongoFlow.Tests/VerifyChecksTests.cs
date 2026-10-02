namespace MongoFlow.Tests;

/// <summary>Verify's own checks: the .editorconfig, .gitattributes and .gitignore settings its snapshots need.</summary>
public class VerifyChecksTests
{
    [Test]
    public Task Run() => VerifyChecks.Run();
}

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="OperationResult.Of"/>: a single-document write changes 0 or 1 documents of each kind, so those results are
/// shared instances with the right counts; any other result is a new one.
/// </summary>
public class OperationResultTests
{
    [Test]
    [MatrixDataSource]
    public async Task Of_SingleDocumentCounts_ReturnsASharedResultWithThem([Matrix(0L, 1L)] long inserted,
        [Matrix(0L, 1L)] long matched,
        [Matrix(0L, 1L)] long modified,
        [Matrix(0L, 1L)] long deleted)
    {
        // Act
        var result = OperationResult.Of(inserted, matched, modified, deleted);

        // Assert
        await Assert.That(result).IsEqualTo(new OperationResult(inserted, matched, modified, deleted));
        await Assert.That(OperationResult.Of(inserted, matched, modified, deleted)).IsSameReferenceAs(result);
    }

    [Test]
    public async Task Of_ManyDocuments_ReturnsANewResultWithTheCounts()
    {
        // Arrange
        const long Matched = 5;
        const long Modified = 3;

        // Act
        var result = OperationResult.Of(0, Matched, Modified, 0);

        // Assert
        await Assert.That(result).IsEqualTo(new OperationResult(0, Matched, Modified, 0));
        await Assert.That(OperationResult.Of(0, Matched, Modified, 0)).IsNotSameReferenceAs(result);
    }
}

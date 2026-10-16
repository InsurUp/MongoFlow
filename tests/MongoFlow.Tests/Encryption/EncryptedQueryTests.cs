using MongoFlow.Encryption;

namespace MongoFlow.Tests.Encryption;

/// <summary>
/// <see cref="EncryptedQuery"/> declares the queries an encrypted member allows:
/// <list type="number">
/// <item>it describes itself as what it allows;</item>
/// <item>a range's bounds are ordered, and its contention, sparsity, trim factor and precision within what MongoDB takes.</item>
/// </list>
/// </summary>
public class EncryptedQueryTests
{
    [Test]
    public async Task ToString_Queries_DescribeWhatTheyAllow()
    {
        // Act
        var described = new[]
        {
            EncryptedQuery.Equality.ToString(),
            EncryptedQuery.EqualityWith(2).ToString(),
            EncryptedQuery.Range(0, 100, contention: 4).ToString()
        };

        // Assert
        await Verify(described);
    }

    [Test]
    [Arguments("MinNotBelowMax")]
    [Arguments("NegativePrecision")]
    [Arguments("SparsityBelowOne")]
    [Arguments("SparsityAboveFour")]
    [Arguments("NegativeTrimFactor")]
    [Arguments("NegativeContention")]
    public async Task Range_ArgumentOutsideWhatMongoDbTakes_ThrowsArgumentException(string argument)
    {
        // Arrange
        Func<EncryptedQuery> range = argument switch
        {
            "MinNotBelowMax" => () => EncryptedQuery.Range(10, 10),
            "NegativePrecision" => () => EncryptedQuery.Range(0d, 1d, precision: -1),
            "SparsityBelowOne" => () => EncryptedQuery.Range(0, 10, sparsity: 0),
            "SparsityAboveFour" => () => EncryptedQuery.Range(0, 10, sparsity: 5),
            "NegativeTrimFactor" => () => EncryptedQuery.Range(0, 10, trimFactor: -1),
            _ => () => EncryptedQuery.EqualityWith(-1)
        };

        // Act & Assert
        await Assert.That(range).Throws<ArgumentException>();
    }
}

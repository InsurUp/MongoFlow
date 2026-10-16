using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>
/// A master key you hold yourself: 96 random bytes, kept out of MongoDB, such as in a secret store, and given to every
/// instance of the app. Losing it loses the data; whoever has it and the database can read it.
/// </summary>
/// <example>
/// Generate one with <c>Convert.ToBase64String(RandomNumberGenerator.GetBytes(96))</c>, store the string as a secret, and
/// read it with <see cref="FromBase64"/>.
/// </example>
public sealed class LocalKms : KmsProvider
{
    /// <summary>The length of a local master key, in bytes.</summary>
    public const int KeyLength = 96;

    private readonly byte[] _key;

    /// <param name="masterKey">The master key: 96 bytes.</param>
    /// <exception cref="ArgumentException">It isn't 96 bytes long.</exception>
    public LocalKms(ReadOnlySpan<byte> masterKey)
    {
        if (masterKey.Length != KeyLength)
        {
            throw new ArgumentException(
                $"A local master key is {KeyLength} bytes long, but this one is {masterKey.Length}.", nameof(masterKey));
        }

        _key = masterKey.ToArray();
    }

    internal override string Name => "local";

    internal override IReadOnlyDictionary<string, object> Credentials => new Dictionary<string, object> { ["key"] = _key };

    internal override BsonDocument? MasterKey => null;

    /// <summary>The master key from its base64 form, as a secret store or configuration holds it.</summary>
    /// <exception cref="FormatException">It isn't base64.</exception>
    /// <exception cref="ArgumentException">It isn't 96 bytes long.</exception>
    public static LocalKms FromBase64(string masterKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterKey);

        return new LocalKms(Convert.FromBase64String(masterKey));
    }
}

using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>A key in Google Cloud KMS, which <see cref="GcpKms"/> creates data keys under.</summary>
public sealed class GcpMasterKey
{
    /// <param name="projectId">The key's project.</param>
    /// <param name="location">The key ring's location, such as <c>europe-west1</c> or <c>global</c>.</param>
    /// <param name="keyRing">The key ring.</param>
    /// <param name="keyName">The key's name.</param>
    public GcpMasterKey(string projectId,
        string location,
        string keyRing,
        string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRing);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);

        ProjectId = projectId;
        Location = location;
        KeyRing = keyRing;
        KeyName = keyName;
    }

    /// <summary>The key's project.</summary>
    public string ProjectId { get; }

    /// <summary>The key ring's location.</summary>
    public string Location { get; }

    /// <summary>The key ring.</summary>
    public string KeyRing { get; }

    /// <summary>The key's name.</summary>
    public string KeyName { get; }

    /// <summary>A version of the key; its primary one without.</summary>
    public string? KeyVersion { get; init; }

    /// <summary>A KMS endpoint other than <c>cloudkms.googleapis.com</c>.</summary>
    public string? Endpoint { get; init; }

    internal BsonDocument ToDocument() =>
        new BsonDocument { { "projectId", ProjectId }, { "location", Location }, { "keyRing", KeyRing }, { "keyName", KeyName } }
            .AddIfNotNull("keyVersion", KeyVersion)
            .AddIfNotNull("endpoint", Endpoint);
}

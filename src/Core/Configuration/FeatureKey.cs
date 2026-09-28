namespace MongoFlow;

/// <summary>Identifies a feature so it can be switched off.</summary>
/// <remarks>
/// Keys compare by name, which must be unique among the features of a vault. <c>default(FeatureKey)</c> has no name and
/// is rejected wherever a key is expected.
/// </remarks>
public readonly record struct FeatureKey
{
    public FeatureKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}

namespace MongoFlow;

/// <summary>Identifies a feature so it can be switched off.</summary>
/// <remarks>
/// Keys compare by name. Several features can share a key, such as soft delete added for two interfaces; switching the
/// key off switches all of them off. <c>default(FeatureKey)</c> has no name and is rejected wherever a key is expected.
/// </remarks>
public readonly record struct FeatureKey
{
    /// <summary>A key named <paramref name="name"/>, such as <c>"soft-delete"</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or white space.</exception>
    public FeatureKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>The name the key compares by.</summary>
    public string Name { get; }

    /// <summary>The key's <see cref="Name"/>.</summary>
    public override string ToString() => Name;
}

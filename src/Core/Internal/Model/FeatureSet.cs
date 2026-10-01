namespace MongoFlow;

/// <summary>
/// The features switched off on a collection view. It's immutable and compares by contents, with its hash computed once,
/// so it can key a cache without allocating.
/// </summary>
internal sealed class FeatureSet : IEquatable<FeatureSet>
{
    public static readonly FeatureSet Empty = new([]);

    // Sorted by name, so equal sets have equal arrays.
    private readonly FeatureKey[] _features;
    private readonly int _hash;

    private FeatureSet(FeatureKey[] features)
    {
        _features = features;

        var hash = new HashCode();
        foreach (var feature in features)
        {
            hash.Add(feature);
        }

        _hash = hash.ToHashCode();
    }

    public bool IsEmpty => _features.Length == 0;

    public bool Contains(FeatureKey feature) => Array.IndexOf(_features, feature) >= 0;

    /// <summary>This set with <paramref name="feature"/> switched off too.</summary>
    /// <exception cref="ArgumentException"><paramref name="feature"/> is <c>default(FeatureKey)</c>.</exception>
    public FeatureSet With(FeatureKey feature)
    {
        FeatureKeys.ThrowIfDefault(feature, nameof(feature));

        if (Contains(feature))
        {
            return this;
        }

        FeatureKey[] features = [.. _features, feature];
        Array.Sort(features, static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return new FeatureSet(features);
    }

    public bool Equals(FeatureSet? other) =>
        ReferenceEquals(this, other) ||
        other is not null && _hash == other._hash && _features.AsSpan().SequenceEqual(other._features);

    public override bool Equals(object? obj) => Equals(obj as FeatureSet);

    public override int GetHashCode() => _hash;
}

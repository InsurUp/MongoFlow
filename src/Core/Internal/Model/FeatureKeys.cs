namespace MongoFlow;

internal static class FeatureKeys
{
    public static readonly IReadOnlySet<FeatureKey> None = new HashSet<FeatureKey>();

    public static void ThrowIfDefault(FeatureKey feature, string parameterName)
    {
        if (feature.Name is null)
        {
            throw new ArgumentException("A default FeatureKey has no name.", parameterName);
        }
    }
}

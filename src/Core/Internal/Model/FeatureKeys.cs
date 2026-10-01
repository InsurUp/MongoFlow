namespace MongoFlow;

internal static class FeatureKeys
{
    public static void ThrowIfDefault(FeatureKey feature, string parameterName)
    {
        if (feature.Name is null)
        {
            throw new ArgumentException("A default FeatureKey has no name.", parameterName);
        }
    }
}

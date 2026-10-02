namespace MongoFlow.Identity;

/// <summary>A store that can make a copy of itself with a vault feature switched off, for the managers' <c>Without</c>.</summary>
internal interface IFeatureSwitchableStore<out TStore>
{
    TStore Without(FeatureKey feature);
}

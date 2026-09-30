namespace MongoFlow;

/// <summary>How one collection property of <typeparamref name="TVault"/> is filled.</summary>
internal abstract class CollectionBinding<TVault> where TVault : MongoVault
{
    public abstract void Attach(TVault vault, VaultRuntime runtime);
}

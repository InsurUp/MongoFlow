using System.Linq.Expressions;

namespace MongoFlow;

/// <summary>Soft delete over a member holding <typeparamref name="TValue"/>: a flag, or a deletion timestamp.</summary>
internal sealed class SoftDeleteFeature<TSoftDelete, TValue> : IVaultFeature, IVaultCollectionConfiguration
{
    private readonly Expression<Func<TSoftDelete, TValue>> _member;
    private readonly Expression<Func<TSoftDelete, bool>> _notDeleted;
    private readonly Func<IServiceProvider, TValue> _deletedValue;
    private readonly Func<TSoftDelete, TValue> _getMember;
    private readonly Action<TSoftDelete, TValue> _setMember;

    public SoftDeleteFeature(Expression<Func<TSoftDelete, TValue>> member,
        Expression<Func<TSoftDelete, bool>> notDeleted,
        Func<IServiceProvider, TValue> deletedValue,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(member, parameterName);

        _member = member;
        _notDeleted = notDeleted;
        _deletedValue = deletedValue;
        _getMember = member.Compile();
        _setMember = member.CreateSetter(parameterName);
    }

    public static FeatureKey Key => SoftDeleteFeature.Key;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
        .QueryFilter(_notDeleted)
        .ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (typeof(TDocument).IsAssignableTo(typeof(TSoftDelete)))
        {
            collection.AddInterceptor(
                new SoftDeleteInterceptor<TDocument, TSoftDelete, TValue>(_member, _getMember, _setMember, _deletedValue));
            ((CollectionModelBuilder<TDocument>)collection).ExpectIndex(_member);
        }
    }
}

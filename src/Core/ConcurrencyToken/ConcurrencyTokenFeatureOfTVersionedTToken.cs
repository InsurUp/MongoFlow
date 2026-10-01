using System.Linq.Expressions;
using System.Numerics;

namespace MongoFlow;

/// <summary>Optimistic concurrency over a numeric member of <typeparamref name="TVersioned"/>.</summary>
internal sealed class ConcurrencyTokenFeature<TVersioned, TToken> : IVaultFeature, IVaultCollectionConfiguration
    where TToken : INumber<TToken>
{
    private readonly Expression<Func<TVersioned, TToken>> _token;
    private readonly Func<TVersioned, TToken> _get;
    private readonly Action<TVersioned, TToken> _set;

    public ConcurrencyTokenFeature(Expression<Func<TVersioned, TToken>> token)
    {
        ArgumentNullException.ThrowIfNull(token);

        _token = token;
        _get = token.Compile();
        _set = MemberExpressions.CreateSetter(token, nameof(token));
    }

    public static FeatureKey Key => ConcurrencyTokenFeature.Key;

    public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault.ForEachCollection(this);

    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection)
    {
        if (!typeof(TDocument).IsAssignableTo(typeof(TVersioned)))
        {
            return;
        }

        // The token works on the write models a save builds after every interceptor ran, such as a delete that soft
        // delete turned into an update; no public extension point reaches them, so the token goes to the model.
        ((CollectionModelBuilder<TDocument>)collection).UseConcurrencyToken(new ConcurrencyTokenModel<TDocument, TToken>(
            MemberExpressions.Rebind<TVersioned, TDocument, TToken>(_token),
            document => _get((TVersioned)(object)document!),
            (document, value) => _set((TVersioned)(object)document!, value)));
    }
}

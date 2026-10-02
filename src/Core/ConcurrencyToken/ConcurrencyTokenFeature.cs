using System.Linq.Expressions;
using System.Numerics;

namespace MongoFlow;

/// <summary>
/// The built-in concurrency token: a numeric member that writes made with a document check, so they don't overwrite a
/// change made since the document was read, and increment.
/// </summary>
public static class ConcurrencyTokenFeature
{
    /// <summary>The key of the built-in concurrency token feature, added with <c>UseConcurrencyToken</c>.</summary>
    /// <remarks>
    /// Switched off on a collection view, such as for an administrator's overwrite, writes through the view are neither
    /// checked nor increment the token: a replace stores the token the document holds.
    /// </remarks>
    public static FeatureKey Key { get; } = new("concurrency-token");

    /// <summary>
    /// Adds optimistic concurrency to every collection whose document is a <typeparamref name="TVersioned"/>. Replacing,
    /// updating or deleting a document (<c>Replace</c>, <c>Update</c>, <c>Delete</c>) fails with
    /// <see cref="ConcurrencyException"/> if its token changed since it was read, soft deletes included. Replaces and
    /// updates increment the token, on the document too; a failed or rolled-back save puts it back.
    /// </summary>
    /// <remarks>
    /// <c>UpdateByKey</c> and set-based writes increment the token but can't check it, having no document that was read.
    /// </remarks>
    /// <param name="vault">The vault to add the feature to.</param>
    /// <param name="token">
    /// A settable numeric member with a typed parameter, such as <c>(IVersioned x) =&gt; x.Version</c>, so the type
    /// arguments are inferred.
    /// </param>
    public static IVaultBuilder<TVault> UseConcurrencyToken<TVault, TVersioned, TToken>(this IVaultBuilder<TVault> vault,
        Expression<Func<TVersioned, TToken>> token)
        where TVault : MongoVault
        where TToken : INumber<TToken> =>
        vault.AddFeature(new ConcurrencyTokenFeature<TVersioned, TToken>(token));
}

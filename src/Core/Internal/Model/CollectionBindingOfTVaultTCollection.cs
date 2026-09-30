using System.Linq.Expressions;
using System.Reflection;

namespace MongoFlow;

internal sealed class CollectionBinding<TVault, TCollection>(PropertyInfo property,
    Func<VaultRuntime, TCollection> create)
    : CollectionBinding<TVault>
    where TVault : MongoVault
{
    private readonly Action<TVault, TCollection> _set = CreateSetter(property);

    public override void Attach(TVault vault, VaultRuntime runtime) => _set(vault, create(runtime));

    private static Action<TVault, TCollection> CreateSetter(PropertyInfo property)
    {
        var vault = Expression.Parameter(typeof(TVault), "vault");
        var collection = Expression.Parameter(typeof(TCollection), "collection");

        return Expression.Lambda<Action<TVault, TCollection>>(
                Expression.Call(vault, property.SetMethod!, collection),
                vault,
                collection)
            .Compile();
    }
}

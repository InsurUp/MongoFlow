using System.Linq.Expressions;

namespace MongoFlow;

public sealed class VaultSoftDeleteOptions<TInterface>
{
    public required Expression<Func<TInterface, bool>> IsDeletedAccessor { get; init; }
    public required Action<TInterface, bool> ChangeIsDeleted { get; init; }
}

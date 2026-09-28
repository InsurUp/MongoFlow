using System.Linq.Expressions;

namespace MongoFlow;

public sealed class VaultMultiTenancyOptions<TInterface, TTenantId> where TTenantId : struct
{
    public required Expression<Func<TInterface, TTenantId?>> TenantIdAccessor { get; init; }

    private Func<TInterface, TTenantId?>? _compiledTenantIdAccessor;

    public Func<TInterface, TTenantId?> TenantIdGetter => _compiledTenantIdAccessor ??= TenantIdAccessor.Compile();

    public required Func<IServiceProvider, TTenantId?> TenantIdProvider { get; init; }

    public required Action<TInterface, TTenantId> TenantIdSetter { get; init; }
}

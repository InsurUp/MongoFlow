using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MongoFlow.Identity;

/// <summary>
/// The <see cref="RoleManager{TRole}"/> <c>AddMongoFlowStores</c> registers: one that can make a copy of itself whose store
/// has a vault feature switched off.
/// </summary>
internal sealed class RoleManagerWrapper<TRole> : RoleManager<TRole> where TRole : class
{
    private readonly IEnumerable<IRoleValidator<TRole>> _roleValidators;
    private readonly ILogger<RoleManager<TRole>> _logger;

    public RoleManagerWrapper(IRoleStore<TRole> store,
        IEnumerable<IRoleValidator<TRole>> roleValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        ILogger<RoleManager<TRole>> logger)
        : base(store, roleValidators, keyNormalizer, errors, logger)
    {
        _roleValidators = roleValidators;
        _logger = logger;
    }

    public RoleManager<TRole> Without(FeatureKey feature) =>
        new RoleManagerWrapper<TRole>(((IFeatureSwitchableStore<IRoleStore<TRole>>)Store).Without(feature),
            _roleValidators,
            KeyNormalizer,
            ErrorDescriber,
            _logger);
}

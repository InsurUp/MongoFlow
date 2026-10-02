using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MongoFlow.Identity;

/// <summary>
/// The <see cref="UserManager{TUser}"/> <c>AddMongoFlowStores</c> registers: one that can make a copy of itself whose store
/// has a vault feature switched off.
/// </summary>
internal sealed class UserManagerWrapper<TUser> : UserManager<TUser> where TUser : class
{
    private readonly IOptions<IdentityOptions> _optionsAccessor;
    private readonly IEnumerable<IUserValidator<TUser>> _userValidators;
    private readonly IEnumerable<IPasswordValidator<TUser>> _passwordValidators;
    private readonly IServiceProvider _services;
    private readonly ILogger<UserManager<TUser>> _logger;

    public UserManagerWrapper(IUserStore<TUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<TUser> passwordHasher,
        IEnumerable<IUserValidator<TUser>> userValidators,
        IEnumerable<IPasswordValidator<TUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<TUser>> logger)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
        _optionsAccessor = optionsAccessor;
        _userValidators = userValidators;
        _passwordValidators = passwordValidators;
        _services = services;
        _logger = logger;
    }

    public UserManager<TUser> Without(FeatureKey feature) =>
        new UserManagerWrapper<TUser>(((IFeatureSwitchableStore<IUserStore<TUser>>)Store).Without(feature),
            _optionsAccessor,
            PasswordHasher,
            _userValidators,
            _passwordValidators,
            KeyNormalizer,
            ErrorDescriber,
            _services,
            _logger);
}

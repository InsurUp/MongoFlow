using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MongoFlow.Identity.Wrappers;

internal sealed class UserManagerWrapper<TUser> : UserManager<TUser> where TUser : class
{
    private readonly DisableContext _queryFilterDisableContext;
    private readonly DisableContext _interceptorDisableContext;
    
    private readonly IUserStore<TUser> _store;
    private readonly IOptions<IdentityOptions> _optionsAccessor;
    private readonly IPasswordHasher<TUser> _passwordHasher;
    private readonly IEnumerable<IUserValidator<TUser>> _userValidators;
    private readonly IEnumerable<IPasswordValidator<TUser>> _passwordValidators;
    private readonly ILookupNormalizer _keyNormalizer;
    private readonly IdentityErrorDescriber _errors;
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
        : base(store,
            optionsAccessor,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            services,
            logger)
    {
        _store = store;
        _optionsAccessor = optionsAccessor;
        _passwordHasher = passwordHasher;
        _userValidators = userValidators;
        _passwordValidators = passwordValidators;
        _keyNormalizer = keyNormalizer;
        _errors = errors;
        _services = services;
        _logger = logger;
        _queryFilterDisableContext = DisableContext.Empty;
        _interceptorDisableContext = DisableContext.Empty;
    }
    
    private UserManagerWrapper(IUserStore<TUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<TUser> passwordHasher,
        IEnumerable<IUserValidator<TUser>> userValidators,
        IEnumerable<IPasswordValidator<TUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services, 
        ILogger<UserManager<TUser>> logger,
        DisableContext queryFilterDisableContext,
        DisableContext interceptorDisableContext)
        : this((store as ICloneUserStore<TUser>)?.Clone(queryFilterDisableContext, interceptorDisableContext) ?? store,
            optionsAccessor,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            services,
            logger)
    {
        _queryFilterDisableContext = queryFilterDisableContext;
        _interceptorDisableContext = interceptorDisableContext;
    }
    
    internal UserManager<TUser> DisableQueryFilters(params string[] names)
    {
        return Clone(_queryFilterDisableContext.Disable(names), _interceptorDisableContext);
    }

    internal UserManager<TUser> DisableAllQueryFilters()
    {
        return Clone(DisableContext.All, _interceptorDisableContext);
    }
    
    internal UserManager<TUser> DisableInterceptors(params string[] names)
    {
        return Clone(_queryFilterDisableContext, _interceptorDisableContext.Disable(names));
    }
    
    internal UserManager<TUser> DisableAllInterceptors()
    {
        return Clone(_queryFilterDisableContext, DisableContext.All);
    }
    
    private UserManager<TUser> Clone(DisableContext queryFilterDisableContext, DisableContext interceptorDisableContext)
    {
        return new UserManagerWrapper<TUser>(_store,
            _optionsAccessor,
            _passwordHasher,
            _userValidators,
            _passwordValidators,
            _keyNormalizer,
            _errors,
            _services,
            _logger,
            queryFilterDisableContext,
            interceptorDisableContext);
    }
}
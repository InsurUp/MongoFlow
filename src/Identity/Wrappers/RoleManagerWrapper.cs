using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MongoFlow.Identity.Wrappers;

internal sealed class RoleManagerWrapper<TRole> : RoleManager<TRole> where TRole : class
{
    private readonly IRoleStore<TRole> _store;
    private readonly IEnumerable<IRoleValidator<TRole>> _roleValidators;
    private readonly ILookupNormalizer _keyNormalizer;
    private readonly IdentityErrorDescriber _errors;
    private readonly ILogger<RoleManager<TRole>> _logger;
    
    private readonly DisableContext _queryFilterDisableContext;
    private readonly DisableContext _interceptorDisableContext;
    
    public RoleManagerWrapper(IRoleStore<TRole> store, 
        IEnumerable<IRoleValidator<TRole>> roleValidators, 
        ILookupNormalizer keyNormalizer, 
        IdentityErrorDescriber errors, 
        ILogger<RoleManager<TRole>> logger) : base(store, roleValidators, keyNormalizer, errors, logger)
    {
        _store = store;
        _roleValidators = roleValidators;
        _keyNormalizer = keyNormalizer;
        _errors = errors;
        _logger = logger;
        _queryFilterDisableContext = DisableContext.Empty;
        _interceptorDisableContext = DisableContext.Empty;
    }
    
    private RoleManagerWrapper(IRoleStore<TRole> store, 
        IEnumerable<IRoleValidator<TRole>> roleValidators, 
        ILookupNormalizer keyNormalizer, 
        IdentityErrorDescriber errors, 
        ILogger<RoleManager<TRole>> logger,
        DisableContext queryFilterDisableContext,
        DisableContext interceptorDisableContext) : this(store, roleValidators, keyNormalizer, errors, logger)
    {
        _queryFilterDisableContext = queryFilterDisableContext;
        _interceptorDisableContext = interceptorDisableContext;
    }
    
    internal RoleManager<TRole> DisableQueryFilters(params string[] names)
    {
        return Clone(_queryFilterDisableContext.Disable(names), _interceptorDisableContext);
    }

    internal RoleManager<TRole> DisableAllQueryFilters()
    {
        return Clone(DisableContext.All, _interceptorDisableContext);
    }
    
    internal RoleManager<TRole> DisableInterceptors(params string[] names)
    {
        return Clone(_queryFilterDisableContext, _interceptorDisableContext.Disable(names));
    }
    
    internal RoleManager<TRole> DisableAllInterceptors()
    {
        return Clone(_queryFilterDisableContext, DisableContext.All);
    }
    
    private RoleManager<TRole> Clone(DisableContext queryFilterDisableContext, DisableContext interceptorDisableContext)
    {
        return new RoleManagerWrapper<TRole>(_store,
            _roleValidators,
            _keyNormalizer,
            _errors,
            _logger,
            queryFilterDisableContext,
            interceptorDisableContext);
    }
}
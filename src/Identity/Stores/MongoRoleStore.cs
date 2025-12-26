using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace MongoFlow.Identity;

public class MongoRoleStore<TVault, TRole, TKey> : IQueryableRoleStore<TRole>, IRoleClaimStore<TRole>, ICloneRoleStore<TRole>
    where TVault : MongoVault
    where TRole : MongoRole<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly DocumentSet<TRole> _roles;
    
    private readonly IdentityErrorDescriber? _describer;
    
    public MongoRoleStore(TVault vault, IdentityErrorDescriber? describer = null)
    {
        ArgumentNullException.ThrowIfNull(vault);

        _describer = describer;
        Vault = vault;
        ErrorDescriber = describer ?? new IdentityErrorDescriber();
        _roles = vault.Set<TRole>();
    }
    
    private MongoRoleStore(TVault vault, 
        IdentityErrorDescriber? describer, 
        DisableContext queryFilterDisableContext, 
        DisableContext interceptorDisableContext)
        : this(vault, describer)
    {
        var roles = vault.Set<TRole>();
        
        roles = queryFilterDisableContext switch
        {
            { AllDisabled: true } => roles.DisableAllQueryFilters(),
            { DisabledItems.Length: > 0 } => roles.DisableQueryFilters(interceptorDisableContext.DisabledItems),
            _ => roles
        };
        
        roles = interceptorDisableContext switch
        {
            { AllDisabled: true } => roles.DisableAllInterceptors(),
            { DisabledItems.Length: > 0 } => roles.DisableInterceptors(interceptorDisableContext.DisabledItems),
            _ => roles
        };
        
        _roles = roles;
    }

    private bool _disposed;
    
    public virtual TVault Vault { get; private set; }

    public IdentityErrorDescriber ErrorDescriber { get; set; }

    public virtual async Task<IdentityResult> CreateAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        
        _roles.Add(role);
        await Vault.SaveAsync(cancellationToken);
        
        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> UpdateAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        
        _roles.Replace(role);
        await Vault.SaveAsync(cancellationToken);
       
        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> DeleteAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        
        _roles.Delete(role);
        await Vault.SaveAsync(cancellationToken);
        
        return IdentityResult.Success;
    }

    public virtual Task<string> GetRoleIdAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        return Task.FromResult(ConvertIdToString(role.Id)!);
    }

    public virtual Task<string?> GetRoleNameAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        return Task.FromResult(role.Name);
    }

    public virtual Task SetRoleNameAsync(TRole role, string? roleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public virtual TKey? ConvertIdFromString(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return default;
        }
        
        return (TKey?)TypeDescriptor.GetConverter(typeof(TKey)).ConvertFromInvariantString(id);
    }

    public virtual string? ConvertIdToString(TKey id)
    {
        if (id.Equals(default))
        {
            return null;
        }
        
        return id.ToString();
    }

    public virtual async Task<TRole?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var roleId = ConvertIdFromString(id);
        var filter = Builders<TRole>.Filter.Eq(x => x.Id, roleId);
        return await _roles.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<TRole?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var filter = Builders<TRole>.Filter.Eq(x => x.NormalizedName, normalizedName);
        return await _roles.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public virtual Task<string?> GetNormalizedRoleNameAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        return Task.FromResult(role.NormalizedName);
    }

    public virtual Task SetNormalizedRoleNameAsync(TRole role, string? normalizedName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose() => _disposed = true;

    public virtual Task<IList<Claim>> GetClaimsAsync(TRole role, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult<IList<Claim>>(role.Claims
            .Select(c => c.ToClaim())
            .ToList());
    }

    public virtual Task AddClaimAsync(TRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(claim);

        role.Claims.Add(new IdentityRoleClaim<TKey>()
        {
            ClaimType = claim.Type,
            ClaimValue = claim.Value
        });
        
        _roles.Replace(role);
        
        return Task.CompletedTask;
    }

    public virtual Task RemoveClaimAsync(TRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(claim);
        
        var roleClaim = role.Claims
            .FirstOrDefault(rc => rc.ClaimValue == claim.Value && rc.ClaimType == claim.Type);

        if (roleClaim != null)
        {
            role.Claims.Remove(roleClaim);
            _roles.Replace(role);
        }
        
        return Task.CompletedTask;
    }

    public virtual IQueryable<TRole> Roles => _roles.AsQueryable();
    
    public IRoleStore<TRole> Clone(DisableContext queryFilterDisableContext, DisableContext interceptorDisableContext)
    {
        return new MongoRoleStore<TVault, TRole, TKey>(Vault, _describer, queryFilterDisableContext, interceptorDisableContext);
    }
}

public interface ICloneRoleStore<TRole> where TRole : class
{
    IRoleStore<TRole> Clone(DisableContext queryFilterDisableContext, DisableContext interceptorDisableContext);
}
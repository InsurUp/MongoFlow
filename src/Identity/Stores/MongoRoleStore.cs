using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver.Linq;

namespace MongoFlow.Identity;

public class MongoRoleStore<TVault, TRole, TKey> : IQueryableRoleStore<TRole>, IRoleClaimStore<TRole>
    where TVault : MongoVault
    where TRole : MongoRole<TKey>
    where TKey : IEquatable<TKey>
{
    public MongoRoleStore(TVault vault, IdentityErrorDescriber? describer = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        Vault = vault;
        ErrorDescriber = describer ?? new IdentityErrorDescriber();
    }

    private bool _disposed;
    
    private DocumentSet<TRole> RoleSet => Vault.Set<TRole>();

    public virtual TVault Vault { get; private set; }

    public IdentityErrorDescriber ErrorDescriber { get; set; }

    public virtual async Task<IdentityResult> CreateAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        
        RoleSet.Add(role);
        await Vault.SaveAsync(cancellationToken);
        
        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> UpdateAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        
        RoleSet.Replace(role);
        await Vault.SaveAsync(cancellationToken);
       
        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> DeleteAsync(TRole role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        
        RoleSet.Delete(role);
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

    public virtual Task<TRole?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var roleId = ConvertIdFromString(id);
        return Roles.FirstOrDefaultAsync(u => u.Id.Equals(roleId), cancellationToken)!;
    }

    public virtual Task<TRole?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, cancellationToken)!;
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
        
        RoleSet.Replace(role);
        
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
            RoleSet.Replace(role);
        }
        
        return Task.CompletedTask;
    }

    public virtual IQueryable<TRole> Roles => RoleSet.AsQueryable();
}
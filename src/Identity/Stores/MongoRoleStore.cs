using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace MongoFlow.Identity;

/// <summary>
/// Identity's role store over a vault's roles collection. Reads apply the vault's query filters, and writes are saved
/// through the vault, so its features and interceptors apply.
/// </summary>
/// <remarks>
/// Changes to a role's claims are made on the role, and saved by <see cref="UpdateAsync"/>, which
/// <see cref="RoleManager{TRole}"/> calls after each.
/// </remarks>
public class MongoRoleStore<TVault, TRole, TKey> : IQueryableRoleStore<TRole>,
    IRoleClaimStore<TRole>,
    IFeatureSwitchableStore<IRoleStore<TRole>>
    where TVault : MongoVault
    where TRole : MongoRole<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly IVaultCollection<TRole, TKey> _roles;
    private bool _disposed;

    public MongoRoleStore(TVault vault,
        IdentityErrorDescriber? describer = null)
        : this(vault, describer, RolesOf(vault))
    {
    }

    private MongoRoleStore(TVault vault,
        IdentityErrorDescriber? describer,
        IVaultCollection<TRole, TKey> roles)
    {
        Vault = vault;
        ErrorDescriber = describer ?? new IdentityErrorDescriber();
        _roles = roles;
    }

    public virtual TVault Vault { get; }

    public IdentityErrorDescriber ErrorDescriber { get; set; }

    /// <summary>The roles, with the vault's query filters applied.</summary>
    /// <remarks>Synchronous, as Identity declares it: asynchronous query filters are waited for.</remarks>
    public virtual IQueryable<TRole> Roles => VaultQueries.Resolve(_roles.QueryAsync());

    public virtual async Task<IdentityResult> CreateAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        _roles.Add(role);
        await Vault.SaveAsync(cancellationToken);

        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> UpdateAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        _roles.Replace(role);
        await Vault.SaveAsync(cancellationToken);

        return IdentityResult.Success;
    }

    public virtual async Task<IdentityResult> DeleteAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        _roles.Delete(role);
        await Vault.SaveAsync(cancellationToken);

        return IdentityResult.Success;
    }

    public virtual Task<string> GetRoleIdAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult(ConvertIdToString(role.Id)!);
    }

    public virtual Task<string?> GetRoleNameAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult(role.Name);
    }

    public virtual Task SetRoleNameAsync(TRole role,
        string? roleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        role.Name = roleName;
        return Task.CompletedTask;
    }

    public virtual Task<string?> GetNormalizedRoleNameAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult(role.NormalizedName);
    }

    public virtual Task SetNormalizedRoleNameAsync(TRole role,
        string? normalizedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public virtual TKey? ConvertIdFromString(string? id) => IdentityKeys.FromString<TKey>(id);

    /// <summary>The id as a string, or <see langword="null"/> while it isn't set: <c>null</c>, or a struct's default.</summary>
    public virtual string? ConvertIdToString(TKey id) => EqualityComparer<TKey>.Default.Equals(id, default) ? null : id!.ToString();

    public virtual async Task<TRole?> FindByIdAsync(string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return ConvertIdFromString(id) is { } roleId ? await _roles.GetByKeyAsync(roleId, cancellationToken) : null;
    }

    public virtual async Task<TRole?> FindByNameAsync(string normalizedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var find = await _roles.FindAsync(Builders<TRole>.Filter.Eq(x => x.NormalizedName, normalizedName), cancellationToken);
        return await find.FirstOrDefaultAsync(cancellationToken);
    }

    public virtual Task<IList<Claim>> GetClaimsAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult<IList<Claim>>(role.Claims.Select(claim => claim.ToClaim()).ToList());
    }

    public virtual Task AddClaimAsync(TRole role,
        Claim claim,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(claim);

        role.Claims.Add(new IdentityRoleClaim<TKey> { ClaimType = claim.Type, ClaimValue = claim.Value });
        return Task.CompletedTask;
    }

    public virtual Task RemoveClaimAsync(TRole role,
        Claim claim,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(claim);

        if (role.Claims.FirstOrDefault(existing => existing.ClaimType == claim.Type && existing.ClaimValue == claim.Value)
            is { } roleClaim)
        {
            role.Claims.Remove(roleClaim);
        }

        return Task.CompletedTask;
    }

    /// <summary>A copy of the store whose reads and writes are made with <paramref name="feature"/> switched off.</summary>
    public virtual MongoRoleStore<TVault, TRole, TKey> Without(FeatureKey feature) =>
        new(Vault, ErrorDescriber, _roles.Without(feature));

    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    IRoleStore<TRole> IFeatureSwitchableStore<IRoleStore<TRole>>.Without(FeatureKey feature) => Without(feature);

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static IVaultCollection<TRole, TKey> RolesOf(TVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return vault.Collection<TRole, TKey>();
    }
}

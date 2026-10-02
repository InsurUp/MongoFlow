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

    /// <summary>A store over the roles of <paramref name="vault"/>.</summary>
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

    /// <summary>The vault the roles are read from and saved through.</summary>
    public virtual TVault Vault { get; }

    /// <summary>Describes the errors the store reports.</summary>
    public IdentityErrorDescriber ErrorDescriber { get; set; }

    /// <summary>The roles, with the vault's query filters applied.</summary>
    /// <remarks>Synchronous, as Identity declares it: asynchronous query filters are waited for.</remarks>
    public virtual IQueryable<TRole> Roles => VaultQueries.Resolve(_roles.QueryAsync());

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public virtual async Task<IdentityResult> UpdateAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        _roles.Replace(role);

        return await SaveAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<IdentityResult> DeleteAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        _roles.Delete(role);

        return await SaveAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual Task<string> GetRoleIdAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult(ConvertIdToString(role.Id)!);
    }

    /// <inheritdoc/>
    public virtual Task<string?> GetRoleNameAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult(role.Name);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public virtual Task<string?> GetNormalizedRoleNameAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult(role.NormalizedName);
    }

    /// <inheritdoc/>
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

    /// <summary>The key in <paramref name="id"/>, as Identity passes keys, or the default when it's empty.</summary>
    public virtual TKey? ConvertIdFromString(string? id) => IdentityKeys.FromString<TKey>(id);

    /// <summary>The id as a string, or <see langword="null"/> while it isn't set: <c>null</c>, or a struct's default.</summary>
    public virtual string? ConvertIdToString(TKey id) => EqualityComparer<TKey>.Default.Equals(id, default) ? null : id!.ToString();

    /// <inheritdoc/>
    public virtual async Task<TRole?> FindByIdAsync(string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return ConvertIdFromString(id) is { } roleId ? await _roles.GetByKeyAsync(roleId, cancellationToken) : null;
    }

    /// <inheritdoc/>
    public virtual async Task<TRole?> FindByNameAsync(string normalizedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var find = await _roles.FindAsync(Builders<TRole>.Filter.Eq(x => x.NormalizedName, normalizedName), cancellationToken);
        return await find.FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public virtual Task<IList<Claim>> GetClaimsAsync(TRole role,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);

        return Task.FromResult<IList<Claim>>(role.Claims.Select(claim => claim.ToClaim()).ToList());
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public virtual Task RemoveClaimAsync(TRole role,
        Claim claim,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(claim);

        var matching = role.Claims.Where(existing => existing.ClaimType == claim.Type && existing.ClaimValue == claim.Value).ToList();
        foreach (var roleClaim in matching)
        {
            role.Claims.Remove(roleClaim);
        }

        return Task.CompletedTask;
    }

    /// <summary>A copy of the store whose reads and writes are made with <paramref name="feature"/> switched off.</summary>
    public virtual MongoRoleStore<TVault, TRole, TKey> Without(FeatureKey feature) =>
        new(Vault, ErrorDescriber, _roles.Without(feature));

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    IRoleStore<TRole> IFeatureSwitchableStore<IRoleStore<TRole>>.Without(FeatureKey feature) => Without(feature);

    /// <summary>Throws <see cref="ObjectDisposedException"/> once the store is disposed.</summary>
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static IVaultCollection<TRole, TKey> RolesOf(TVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return vault.Collection<TRole, TKey>();
    }

    /// <summary>Saves the vault, reporting a role changed or deleted since it was read as Identity's concurrency failure.</summary>
    private async Task<IdentityResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Vault.SaveAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (ConcurrencyStampConflictException)
        {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }
        catch (ClientBulkWriteException exception) when (IdentityWrites.IsWriteConflict(exception))
        {
            // Another request's transaction is changing the role: it fails as if that change were committed.
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }
    }
}

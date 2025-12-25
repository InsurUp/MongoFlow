using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow.Identity;

public class MongoUserStore<TVault, TUser, TRole, TKey> : 
    UserStoreBase<TUser, TRole, TKey, IdentityUserClaim<TKey>, IdentityUserRole<TKey>, IdentityUserLogin<TKey>, MongoUserToken<TKey>, IdentityRoleClaim<TKey>>,
    ICloneUserStore<TUser>, IUserPasskeyStore<TUser>
    where TVault : IdentityMongoVault<TUser, TRole, TKey>
    where TUser : MongoUser<TKey>
    where TRole : MongoRole<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly TVault _vault;
    private readonly IdentityErrorDescriber _describer;
    private readonly DocumentSet<TUser> _users;
    private readonly DocumentSet<TRole> _roles;
    private readonly DocumentSet<MongoUserToken<TKey>> _userTokens;

    public MongoUserStore(TVault vault, IdentityErrorDescriber describer) : base(describer)
    {
        _vault = vault;
        _describer = describer;
        _users = _vault.Set<TUser>();
        _roles = _vault.Set<TRole>();
        _userTokens = _vault.Set<MongoUserToken<TKey>>();
    }
    
    private MongoUserStore(TVault vault, 
        IdentityErrorDescriber describer,
        DisableContext queryFilterDisableContext,
        DisableContext interceptorDisableContext) : this(vault, describer)
    {
        var users = _vault.Set<TUser>();
        var roles = _vault.Set<TRole>();
        var userTokens = _vault.Set<MongoUserToken<TKey>>();

        users = queryFilterDisableContext switch
        {
            { AllDisabled: true } => users.DisableAllQueryFilters(),
            { DisabledItems.Length: > 0 } => users.DisableQueryFilters(interceptorDisableContext.DisabledItems),
            _ => users
        };

        users = interceptorDisableContext switch
        {
            { AllDisabled: true } => users.DisableAllInterceptors(),
            { DisabledItems.Length: > 0 } => users.DisableInterceptors(interceptorDisableContext.DisabledItems),
            _ => users
        };
        
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
        
        userTokens = queryFilterDisableContext switch
        {
            { AllDisabled: true } => userTokens.DisableAllQueryFilters(),
            { DisabledItems.Length: > 0 } => userTokens.DisableQueryFilters(interceptorDisableContext.DisabledItems),
            _ => userTokens
        };
        
        userTokens = interceptorDisableContext switch
        {
            { AllDisabled: true } => userTokens.DisableAllInterceptors(),
            { DisabledItems.Length: > 0 } => userTokens.DisableInterceptors(interceptorDisableContext.DisabledItems),
            _ => userTokens
        };

        _users = users;
        _roles = roles;
        _userTokens = userTokens;
    }

    public override async Task<IdentityResult> CreateAsync(TUser user, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        
        _users.Add(user);
        await _vault.SaveAsync(cancellationToken);
        
        return IdentityResult.Success;
    }

    public override async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        
        _users.Replace(user);
        await _vault.SaveAsync(cancellationToken);
        
        return IdentityResult.Success;
    }

    public override async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        
        _users.Delete(user);
        await _vault.SaveAsync(cancellationToken);
        
        return IdentityResult.Success;
    }

    public override async Task<TUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(userId);
        
        var id = ConvertIdFromString(userId);
        if (id is null)
        {
            return null;
        }
        
        return await _users.GetByKeyAsync(id, cancellationToken);
    }

    public override async Task<TUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(normalizedUserName);
        
        return await _users
            .Find(x => x.NormalizedUserName == normalizedUserName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    protected override async Task<TUser?> FindUserAsync(TKey userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        
        return await _users.GetByKeyAsync(userId, cancellationToken);
    }

    protected override async Task<IdentityUserLogin<TKey>?> FindUserLoginAsync(TKey userId, string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        
        var result = await _users.AsQueryable()
            .Where(x => x.Id.Equals(userId))
            .SelectMany(x => x.Logins)
            .Where(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey)
            .FirstOrDefaultAsync(cancellationToken);
        
        return result;
    }

    protected override async Task<IdentityUserLogin<TKey>?> FindUserLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        
        var user = await _users
            .Find(x => x.Logins.Any(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey))
            .FirstOrDefaultAsync(cancellationToken);
        
        return user?.Logins.FirstOrDefault(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey);
    }

    public override Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IList<Claim>>(user.Claims
            .Select(x => x.ToClaim())
            .ToList());
    }

    public override Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claims);
        
        foreach (var claim in claims)
        {
            user.Claims.Add(new IdentityUserClaim<TKey>
            {
                ClaimType = claim.Type,
                ClaimValue = claim.Value
            });
        }
        
        _users.Replace(user);
        
        return Task.CompletedTask;
    }

    public override Task ReplaceClaimAsync(TUser user, Claim claim, Claim newClaim,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(newClaim);
        
        var userClaim = user.Claims.FirstOrDefault(x => x.ClaimType == claim.Type && x.ClaimValue == claim.Value);
        
        if (userClaim is not null)
        {
            userClaim.ClaimType = newClaim.Type;
            userClaim.ClaimValue = newClaim.Value;
        }
        
        _users.Replace(user);
        
        return Task.CompletedTask;
    }

    public override Task RemoveClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claims);
        
        foreach (var claim in claims)
        {
            var userClaim = user.Claims.FirstOrDefault(x => x.ClaimType == claim.Type && x.ClaimValue == claim.Value);
            if (userClaim is not null)
            {
                user.Claims.Remove(userClaim);
            }
        }
        
        _users.Replace(user);
        
        return Task.CompletedTask;
    }

    public override async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(claim);
        
        return await _users.AsQueryable()
            .Where(x => x.Claims.Any(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value))
            .ToListAsync(cancellationToken);
    }

    protected override async Task<MongoUserToken<TKey>?> FindTokenAsync(TUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        
        return await _userTokens
            .Find(x => x.UserId.Equals(user.Id) && x.LoginProvider == loginProvider && x.Name == name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    protected override Task AddUserTokenAsync(MongoUserToken<TKey> token)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(token);
        
        _userTokens.Add(token);
        
        return Task.CompletedTask;
    }

    protected override async Task RemoveUserTokenAsync(MongoUserToken<TKey> token)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(token);
        
        var existingToken = await _userTokens
            .Find(x => x.UserId.Equals(token.UserId) && x.LoginProvider == token.LoginProvider && x.Name == token.Name)
            .FirstOrDefaultAsync();
        
        if (existingToken is not null)
        {
            _userTokens.Delete(existingToken);
        }
    }

    public override IQueryable<TUser> Users => _users.AsQueryable();

    public override Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(login);
        
        user.Logins.Add(new IdentityUserLogin<TKey>
        {
            LoginProvider = login.LoginProvider,
            ProviderKey = login.ProviderKey,
            ProviderDisplayName = login.ProviderDisplayName
        });
        
        _users.Replace(user);
        
        return Task.CompletedTask;
    }

    public override Task RemoveLoginAsync(TUser user, 
        string loginProvider, 
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        
        var login = user.Logins.FirstOrDefault(x => x.LoginProvider == loginProvider && x.ProviderKey == providerKey);
        if (login is not null)
        {
            user.Logins.Remove(login);
        }
        
        _users.Replace(user);
        
        return Task.CompletedTask;
    }

    public override Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IList<UserLoginInfo>>(user.Logins
            .Select(x => new UserLoginInfo(x.LoginProvider, x.ProviderKey, x.ProviderDisplayName))
            .ToList());
    }

    public override async Task<TUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        
        return await _users
            .Find(x => x.NormalizedEmail == normalizedEmail)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public override async Task<bool> IsInRoleAsync(TUser user, string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(normalizedRoleName);
        
        var roleIds = user.Roles;
        
        return await _roles
            .Find(x => x.NormalizedName == normalizedRoleName && roleIds.Contains(x.Id))
            .AnyAsync(cancellationToken);
    }

    protected override async Task<TRole?> FindRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(normalizedRoleName);
        
        return await _roles
            .Find(x => x.NormalizedName == normalizedRoleName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    protected override async Task<IdentityUserRole<TKey>?> FindUserRoleAsync(TKey userId, TKey roleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        
        return await _users.AsQueryable()
            .Where(x => x.Id.Equals(userId))
            .SelectMany(x => x.Roles)
            .Where(x => x.Equals(roleId))
            .Select(x => new IdentityUserRole<TKey>
            {
                UserId = userId,
                RoleId = x
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public override async Task<IList<TUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(normalizedRoleName);
        
        var role = await FindRoleAsync(normalizedRoleName, cancellationToken);
        if (role is null)
        {
            return new List<TUser>();
        }
        
        return await _users.AsQueryable()
            .Where(x => x.Roles.Contains(role.Id))
            .ToListAsync(cancellationToken);
    }

    public override async Task AddToRoleAsync(TUser user, string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(normalizedRoleName);
        
        var role = await FindRoleAsync(normalizedRoleName, cancellationToken);
        if (role is null)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Role {0} does not exist.", normalizedRoleName));
        }
        
        user.Roles.Add(role.Id);
        
        _users.Replace(user);
    }

    public override async Task RemoveFromRoleAsync(TUser user, 
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(normalizedRoleName);
        
        var role = await FindRoleAsync(normalizedRoleName, cancellationToken);
        if (role is null)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Role {0} does not exist.", normalizedRoleName));
        }
        
        user.Roles.Remove(role.Id);
        
        _users.Replace(user);
    }

    public override async Task<IList<string>> GetRolesAsync(TUser user, CancellationToken cancellationToken = default)
    {
        var roleIds = user.Roles;
        
        return await _roles.Find(x => roleIds.Contains(x.Id) && x.Name != null)
            .Project(x => x.Name!)
            .ToListAsync(cancellationToken);
    }
    
    public IUserStore<TUser> Clone(DisableContext queryFilterDisableContext, DisableContext interceptorDisableContext)
    {
        return new MongoUserStore<TVault, TUser, TRole, TKey>(_vault, _describer, queryFilterDisableContext, interceptorDisableContext);
    }

    public Task AddOrUpdatePasskeyAsync(TUser user, UserPasskeyInfo passkey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(passkey);

        var existingPasskey = user.Passkeys.FirstOrDefault(p => p.CredentialId.SequenceEqual(passkey.CredentialId));
        
        if (existingPasskey is not null)
        {
            existingPasskey.Data = new IdentityPasskeyData
            {
                PublicKey = passkey.PublicKey,
                Name = passkey.Name,
                CreatedAt = passkey.CreatedAt,
                SignCount = passkey.SignCount,
                Transports = passkey.Transports,
                IsUserVerified = passkey.IsUserVerified,
                IsBackupEligible = passkey.IsBackupEligible,
                IsBackedUp = passkey.IsBackedUp,
                AttestationObject = passkey.AttestationObject,
                ClientDataJson = passkey.ClientDataJson
            };
        }
        else
        {
            user.Passkeys.Add(new IdentityUserPasskey<TKey>
            {
                UserId = user.Id,
                CredentialId = passkey.CredentialId,
                Data = new IdentityPasskeyData
                {
                    PublicKey = passkey.PublicKey,
                    Name = passkey.Name,
                    CreatedAt = passkey.CreatedAt,
                    SignCount = passkey.SignCount,
                    Transports = passkey.Transports,
                    IsUserVerified = passkey.IsUserVerified,
                    IsBackupEligible = passkey.IsBackupEligible,
                    IsBackedUp = passkey.IsBackedUp,
                    AttestationObject = passkey.AttestationObject,
                    ClientDataJson = passkey.ClientDataJson
                }
            });
        }
        
        _users.Replace(user);
        
        return Task.CompletedTask;
    }

    public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(TUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult<IList<UserPasskeyInfo>>(user.Passkeys
            .Select(p => new UserPasskeyInfo(
                p.CredentialId,
                p.Data.PublicKey,
                p.Data.CreatedAt,
                p.Data.SignCount,
                p.Data.Transports,
                p.Data.IsUserVerified,
                p.Data.IsBackupEligible,
                p.Data.IsBackedUp,
                p.Data.AttestationObject,
                p.Data.ClientDataJson)
            {
                Name = p.Data.Name
            })
            .ToList());
    }

    public async Task<TUser?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(credentialId);

        return await _users
            .Find(u => u.Passkeys.Any(p => p.CredentialId == credentialId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<UserPasskeyInfo?> FindPasskeyAsync(TUser user, byte[] credentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credentialId);

        var passkey = user.Passkeys.FirstOrDefault(p => p.CredentialId.SequenceEqual(credentialId));
        
        if (passkey is null)
        {
            return Task.FromResult<UserPasskeyInfo?>(null);
        }

        return Task.FromResult<UserPasskeyInfo?>(new UserPasskeyInfo(
            passkey.CredentialId,
            passkey.Data.PublicKey,
            passkey.Data.CreatedAt,
            passkey.Data.SignCount,
            passkey.Data.Transports,
            passkey.Data.IsUserVerified,
            passkey.Data.IsBackupEligible,
            passkey.Data.IsBackedUp,
            passkey.Data.AttestationObject,
            passkey.Data.ClientDataJson)
        {
            Name = passkey.Data.Name
        });
    }

    public Task RemovePasskeyAsync(TUser user, byte[] credentialId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credentialId);

        var passkey = user.Passkeys.FirstOrDefault(p => p.CredentialId.SequenceEqual(credentialId));
        
        if (passkey is not null)
        {
            user.Passkeys.Remove(passkey);
            _users.Replace(user);
        }
        
        return Task.CompletedTask;
    }
}

public interface ICloneUserStore<TUser> where TUser : class
{
    IUserStore<TUser> Clone(DisableContext queryFilterDisableContext, DisableContext interceptorDisableContext);
}
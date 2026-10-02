using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow.Identity;

/// <summary>
/// Identity's user store over an <see cref="IdentityMongoVault{TUser, TRole, TKey}"/>. Reads apply the vault's query
/// filters, and writes are saved through the vault, so its features and interceptors apply.
/// </summary>
/// <remarks>
/// Claims, logins, roles and passkeys are kept inside the user and changed on it; <see cref="UserManager{TUser}"/> saves
/// them with <see cref="UpdateAsync"/> after each change, along with any token the change queued.
/// </remarks>
public class MongoUserStore<TVault, TUser, TRole, TKey> :
    UserStoreBase<TUser, TRole, TKey, IdentityUserClaim<TKey>, IdentityUserRole<TKey>, IdentityUserLogin<TKey>, MongoUserToken<TKey>,
        IdentityRoleClaim<TKey>>,
    IUserPasskeyStore<TUser>,
    IFeatureSwitchableStore<IUserStore<TUser>>
    where TVault : IdentityMongoVault<TUser, TRole, TKey>
    where TUser : MongoUser<TKey>
    where TRole : MongoRole<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly TVault _vault;
    private readonly IVaultCollection<TUser, TKey> _users;
    private readonly IVaultCollection<TRole, TKey> _roles;
    private readonly IVaultCollection<MongoUserToken<TKey>, TKey> _userTokens;

    /// <summary>A store over the users and tokens of <paramref name="vault"/>.</summary>
    public MongoUserStore(TVault vault,
        IdentityErrorDescriber describer)
        : this(vault, describer, vault.Users, vault.Roles, vault.UserTokens)
    {
    }

    private MongoUserStore(TVault vault,
        IdentityErrorDescriber describer,
        IVaultCollection<TUser, TKey> users,
        IVaultCollection<TRole, TKey> roles,
        IVaultCollection<MongoUserToken<TKey>, TKey> userTokens)
        : base(describer)
    {
        _vault = vault;
        _users = users;
        _roles = roles;
        _userTokens = userTokens;
    }

    /// <summary>The users, with the vault's query filters applied.</summary>
    /// <remarks>Synchronous, as Identity declares it: asynchronous query filters are waited for.</remarks>
    public override IQueryable<TUser> Users => VaultQueries.Resolve(_users.QueryAsync());

    /// <inheritdoc/>
    public override async Task<IdentityResult> CreateAsync(TUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        _users.Add(user);
        await _vault.SaveAsync(cancellationToken);

        return IdentityResult.Success;
    }

    /// <inheritdoc/>
    public override async Task<IdentityResult> UpdateAsync(TUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        _users.Replace(user);

        return await SaveAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task<IdentityResult> DeleteAsync(TUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        // The user's tokens, such as its authenticator key and recovery codes, go with it.
        var userId = user.Id;
        _users.Delete(user);
        _userTokens.DeleteMany(token => token.UserId.Equals(userId));

        return await SaveAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override TKey? ConvertIdFromString(string? id) => IdentityKeys.FromString<TKey>(id);

    /// <inheritdoc/>
    public override async Task<TUser?> FindByIdAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(userId);

        return ConvertIdFromString(userId) is { } id ? await _users.GetByKeyAsync(id, cancellationToken) : null;
    }

    /// <inheritdoc/>
    public override Task<TUser?> FindByNameAsync(string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(normalizedUserName);

        return FirstUserAsync(Builders<TUser>.Filter.Eq(x => x.NormalizedUserName, normalizedUserName), cancellationToken);
    }

    /// <inheritdoc/>
    public override Task<TUser?> FindByEmailAsync(string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(normalizedEmail);

        return FirstUserAsync(Builders<TUser>.Filter.Eq(x => x.NormalizedEmail, normalizedEmail), cancellationToken);
    }

    /// <inheritdoc/>
    public override Task<IList<Claim>> GetClaimsAsync(TUser user,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult<IList<Claim>>(user.Claims.Select(claim => claim.ToClaim()).ToList());
    }

    /// <inheritdoc/>
    public override Task AddClaimsAsync(TUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claims);

        foreach (var claim in claims)
        {
            user.Claims.Add(new IdentityUserClaim<TKey> { ClaimType = claim.Type, ClaimValue = claim.Value });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task ReplaceClaimAsync(TUser user,
        Claim claim,
        Claim newClaim,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(newClaim);

        foreach (var userClaim in user.Claims.Where(existing => existing.ClaimType == claim.Type && existing.ClaimValue == claim.Value))
        {
            userClaim.ClaimType = newClaim.Type;
            userClaim.ClaimValue = newClaim.Value;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task RemoveClaimsAsync(TUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claims);

        foreach (var claim in claims)
        {
            var matching = user.Claims.Where(existing => existing.ClaimType == claim.Type && existing.ClaimValue == claim.Value).ToList();
            foreach (var userClaim in matching)
            {
                user.Claims.Remove(userClaim);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(claim);

        var filter = Builders<TUser>.Filter.ElemMatch(x => x.Claims, c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value);
        return await (await _users.FindAsync(filter, cancellationToken)).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override Task AddLoginAsync(TUser user,
        UserLoginInfo login,
        CancellationToken cancellationToken = default)
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

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task RemoveLoginAsync(TUser user,
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (user.Logins.FirstOrDefault(login => login.LoginProvider == loginProvider && login.ProviderKey == providerKey) is { } existing)
        {
            user.Logins.Remove(existing);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult<IList<UserLoginInfo>>(user.Logins
            .Select(login => new UserLoginInfo(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName))
            .ToList());
    }

    /// <inheritdoc/>
    public override async Task<bool> IsInRoleAsync(TUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(normalizedRoleName);

        var filter = Builders<TRole>.Filter.Eq(x => x.NormalizedName, normalizedRoleName) &
                     Builders<TRole>.Filter.In(x => x.Id, user.Roles);

        return await (await _roles.FindAsync(filter, cancellationToken)).AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task AddToRoleAsync(TUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(normalizedRoleName);

        user.Roles.Add((await RequireRoleAsync(normalizedRoleName, cancellationToken)).Id);
    }

    /// <inheritdoc/>
    public override async Task RemoveFromRoleAsync(TUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(normalizedRoleName);

        user.Roles.Remove((await RequireRoleAsync(normalizedRoleName, cancellationToken)).Id);
    }

    /// <inheritdoc/>
    public override async Task<IList<string>> GetRolesAsync(TUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        var filter = Builders<TRole>.Filter.In(x => x.Id, user.Roles) & Builders<TRole>.Filter.Ne(x => x.Name, null);
        return await (await _roles.FindAsync(filter, cancellationToken)).Project(x => x.Name!).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task<IList<TUser>> GetUsersInRoleAsync(string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(normalizedRoleName);

        if (await FindRoleAsync(normalizedRoleName, cancellationToken) is not { } role)
        {
            return [];
        }

        return await (await _users.FindAsync(Builders<TUser>.Filter.AnyEq(x => x.Roles, role.Id), cancellationToken))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Sets the token's value, adding the token if the user has none of that name; saved with the user.</summary>
    public override async Task SetTokenAsync(TUser user,
        string loginProvider,
        string name,
        string? value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (await FindTokenAsync(user, loginProvider, name, cancellationToken) is { } token)
        {
            token.Value = value;
            _userTokens.Replace(token);
        }
        else
        {
            await AddUserTokenAsync(CreateUserToken(user, loginProvider, name, value));
        }
    }

    /// <inheritdoc/>
    public Task AddOrUpdatePasskeyAsync(TUser user,
        UserPasskeyInfo passkey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(passkey);

        var data = new IdentityPasskeyData
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

        if (FindPasskey(user, passkey.CredentialId) is { } existing)
        {
            existing.Data = data;
        }
        else
        {
            user.Passkeys.Add(new IdentityUserPasskey<TKey> { UserId = user.Id, CredentialId = passkey.CredentialId, Data = data });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(TUser user,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult<IList<UserPasskeyInfo>>(user.Passkeys.Select(ToInfo).ToList());
    }

    /// <inheritdoc/>
    public async Task<TUser?> FindByPasskeyIdAsync(byte[] credentialId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(credentialId);

        return await FirstUserAsync(Builders<TUser>.Filter.ElemMatch(x => x.Passkeys, p => p.CredentialId == credentialId),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<UserPasskeyInfo?> FindPasskeyAsync(TUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credentialId);

        return Task.FromResult(FindPasskey(user, credentialId) is { } passkey ? ToInfo(passkey) : null);
    }

    /// <inheritdoc/>
    public Task RemovePasskeyAsync(TUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credentialId);

        if (FindPasskey(user, credentialId) is { } passkey)
        {
            user.Passkeys.Remove(passkey);
        }

        return Task.CompletedTask;
    }

    /// <summary>A copy of the store whose reads and writes are made with <paramref name="feature"/> switched off.</summary>
    public virtual MongoUserStore<TVault, TUser, TRole, TKey> Without(FeatureKey feature) =>
        new(_vault, ErrorDescriber, _users.Without(feature), _roles.Without(feature), _userTokens.Without(feature));

    IUserStore<TUser> IFeatureSwitchableStore<IUserStore<TUser>>.Without(FeatureKey feature) => Without(feature);

    /// <inheritdoc/>
    protected override Task<TUser?> FindUserAsync(TKey userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return _users.GetByKeyAsync(userId, cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task<IdentityUserLogin<TKey>?> FindUserLoginAsync(TKey userId,
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return LoginOf(await _users.GetByKeyAsync(userId, cancellationToken), loginProvider, providerKey);
    }

    /// <inheritdoc/>
    protected override async Task<IdentityUserLogin<TKey>?> FindUserLoginAsync(string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var filter = Builders<TUser>.Filter.ElemMatch(x => x.Logins, l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
        return LoginOf(await FirstUserAsync(filter, cancellationToken), loginProvider, providerKey);
    }

    /// <inheritdoc/>
    protected override async Task<TRole?> FindRoleAsync(string normalizedRoleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var find = await _roles.FindAsync(Builders<TRole>.Filter.Eq(x => x.NormalizedName, normalizedRoleName), cancellationToken);
        return await find.FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task<IdentityUserRole<TKey>?> FindUserRoleAsync(TKey userId,
        TKey roleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var user = await _users.GetByKeyAsync(userId, cancellationToken);
        return user?.Roles.Contains(roleId) == true ? new IdentityUserRole<TKey> { UserId = userId, RoleId = roleId } : null;
    }

    /// <inheritdoc/>
    protected override async Task<MongoUserToken<TKey>?> FindTokenAsync(TUser user,
        string loginProvider,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        var find = await _userTokens.FindAsync(TokenFilter(user.Id, loginProvider, name), cancellationToken);
        return await find.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>A new token of the user's; with string keys, which the driver can't generate, it's given one.</summary>
    protected override MongoUserToken<TKey> CreateUserToken(TUser user,
        string loginProvider,
        string name,
        string? value)
    {
        var token = base.CreateUserToken(user, loginProvider, name, value);
        if (typeof(TKey) == typeof(string))
        {
            token.Id = (TKey)(object)ObjectId.GenerateNewId().ToString();
        }

        return token;
    }

    /// <summary>Queues the token, which is saved with the user.</summary>
    protected override Task AddUserTokenAsync(MongoUserToken<TKey> token)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(token);

        _userTokens.Add(token);
        return Task.CompletedTask;
    }

    /// <summary>Queues the token's removal, which is saved with the user.</summary>
    protected override async Task RemoveUserTokenAsync(MongoUserToken<TKey> token)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(token);

        var find = await _userTokens.FindAsync(TokenFilter(token.UserId, token.LoginProvider, token.Name));
        if (await find.FirstOrDefaultAsync() is { } stored)
        {
            _userTokens.Delete(stored);
        }
    }

    /// <summary>Saves the vault, reporting a user changed or deleted since it was read as Identity's concurrency failure.</summary>
    private async Task<IdentityResult> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _vault.SaveAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (ConcurrencyStampConflictException)
        {
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }
        catch (ClientBulkWriteException exception) when (IdentityWrites.IsWriteConflict(exception))
        {
            // Another request's transaction is changing the user: it fails as if that change were committed.
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
        }
    }

    private static FilterDefinition<MongoUserToken<TKey>> TokenFilter(TKey userId,
        string loginProvider,
        string name) =>
        Builders<MongoUserToken<TKey>>.Filter.Eq(x => x.UserId, userId) &
        Builders<MongoUserToken<TKey>>.Filter.Eq(x => x.LoginProvider, loginProvider) &
        Builders<MongoUserToken<TKey>>.Filter.Eq(x => x.Name, name);

    /// <summary>
    /// The user's login, with its <c>UserId</c>, which isn't stored inside the user: Identity looks the user up by it.
    /// </summary>
    private static IdentityUserLogin<TKey>? LoginOf(TUser? user,
        string loginProvider,
        string providerKey) =>
        user?.Logins.FirstOrDefault(login => login.LoginProvider == loginProvider && login.ProviderKey == providerKey) is { } login
            ? new IdentityUserLogin<TKey>
            {
                UserId = user.Id,
                LoginProvider = login.LoginProvider,
                ProviderKey = login.ProviderKey,
                ProviderDisplayName = login.ProviderDisplayName
            }
            : null;

    private static IdentityUserPasskey<TKey>? FindPasskey(TUser user,
        byte[] credentialId) =>
        user.Passkeys.FirstOrDefault(passkey => passkey.CredentialId.SequenceEqual(credentialId));

    private static UserPasskeyInfo ToInfo(IdentityUserPasskey<TKey> passkey) =>
        new(passkey.CredentialId,
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
        };

    private async Task<TUser?> FirstUserAsync(FilterDefinition<TUser> filter,
        CancellationToken cancellationToken) =>
        await (await _users.FindAsync(filter, cancellationToken)).FirstOrDefaultAsync(cancellationToken);

    private async Task<TRole> RequireRoleAsync(string normalizedRoleName,
        CancellationToken cancellationToken) =>
        await FindRoleAsync(normalizedRoleName, cancellationToken)
        ?? throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Role {0} does not exist.", normalizedRoleName));
}

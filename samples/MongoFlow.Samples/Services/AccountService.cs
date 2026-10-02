using Microsoft.AspNetCore.Identity;
using MongoFlow.Identity;
using MongoFlow.Samples.Vaults;

namespace MongoFlow.Samples.Services;

/// <summary>Platform accounts through ASP.NET Core Identity's managers, stored in a vault by MongoFlow.Identity.</summary>
public sealed class AccountService(UserManager<PlatformUser> users, RoleManager<PlatformRole> roles)
{
    public async Task<PlatformUser> CreateAsync(string email, string password, string role)
    {
        if (!await roles.RoleExistsAsync(role))
        {
            Check(await roles.CreateAsync(new PlatformRole { Name = role }));
        }

        var user = new PlatformUser { UserName = email, Email = email };
        Check(await users.CreateAsync(user, password));
        Check(await users.AddToRoleAsync(user, role));

        return user;
    }

    public Task<PlatformUser?> FindAsync(string email) => users.FindByEmailAsync(email);

    public Task<IList<string>> RolesAsync(PlatformUser user) => users.GetRolesAsync(user);

    // The vault soft-deletes users: a deleted account is kept, and hidden from the manager's reads.
    public async Task DeactivateAsync(PlatformUser user) => Check(await users.DeleteAsync(user));

    // A copy of the manager with soft delete switched off reaches deleted accounts.
    public async Task<PlatformUser?> RestoreAsync(string email)
    {
        var withDeleted = users.Without(SoftDeleteFeature.Key);
        if (await withDeleted.FindByEmailAsync(email) is not { } user)
        {
            return null;
        }

        user.IsDeleted = false;
        Check(await withDeleted.UpdateAsync(user));

        return user;
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
        }
    }
}

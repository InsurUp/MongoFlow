using Microsoft.AspNetCore.Identity;
using MongoFlow.Identity.Wrappers;

namespace MongoFlow.Identity;

public static class UserManagerExtensions
{
    public static UserManager<TUser> DisableAllQueryFilters<TUser>(this UserManager<TUser> userManager) where TUser : class
    {
        if (userManager is UserManagerWrapper<TUser> wrapper)
        {
            return wrapper.DisableAllQueryFilters();
        }

        return userManager;
    }
    
    public static UserManager<TUser> DisableQueryFilters<TUser>(this UserManager<TUser> userManager, params string[] names) where TUser : class
    {
        if (userManager is UserManagerWrapper<TUser> wrapper)
        {
            return wrapper.DisableQueryFilters(names);
        }

        return userManager;
    }
    
    public static UserManager<TUser> DisableAllInterceptors<TUser>(this UserManager<TUser> userManager) where TUser : class
    {
        if (userManager is UserManagerWrapper<TUser> wrapper)
        {
            return wrapper.DisableAllInterceptors();
        }

        return userManager;
    }
    
    public static UserManager<TUser> DisableInterceptors<TUser>(this UserManager<TUser> userManager, params string[] names) where TUser : class
    {
        if (userManager is UserManagerWrapper<TUser> wrapper)
        {
            return wrapper.DisableInterceptors(names);
        }

        return userManager;
    }
    
    public static UserManager<TUser> DisableMultiTenancy<TUser>(this UserManager<TUser> userManager) where TUser : class
    {
        if (userManager is UserManagerWrapper<TUser> wrapper)
        {
            return wrapper
                .DisableQueryFilters("multi-tenancy")
                .DisableInterceptors("multi-tenancy");
        }

        return userManager;
    }
    
    public static UserManager<TUser> DisableSoftDelete<TUser>(this UserManager<TUser> userManager) where TUser : class
    {
        if (userManager is UserManagerWrapper<TUser> wrapper)
        {
            return wrapper
                .DisableQueryFilters("soft-delete")
                .DisableInterceptors("soft-delete");
        }

        return userManager;
    }
    
    
    
}
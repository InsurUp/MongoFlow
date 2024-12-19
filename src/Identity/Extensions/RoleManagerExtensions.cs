using Microsoft.AspNetCore.Identity;
using MongoFlow.Identity.Wrappers;

namespace MongoFlow.Identity;

public static class RoleManagerExtensions
{
    public static RoleManager<TRole> DisableAllQueryFilters<TRole>(this RoleManager<TRole> roleManager) where TRole : class
    {
        if (roleManager is RoleManagerWrapper<TRole> wrapper)
        {
            return wrapper.DisableAllQueryFilters();
        }

        return roleManager;
    }
    
    public static RoleManager<TRole> DisableQueryFilters<TRole>(this RoleManager<TRole> roleManager, params string[] names) where TRole : class
    {
        if (roleManager is RoleManagerWrapper<TRole> wrapper)
        {
            return wrapper.DisableQueryFilters(names);
        }

        return roleManager;
    }
    
    public static RoleManager<TRole> DisableAllInterceptors<TRole>(this RoleManager<TRole> roleManager) where TRole : class
    {
        if (roleManager is RoleManagerWrapper<TRole> wrapper)
        {
            return wrapper.DisableAllInterceptors();
        }

        return roleManager;
    }
    
    public static RoleManager<TRole> DisableInterceptors<TRole>(this RoleManager<TRole> roleManager, params string[] names) where TRole : class
    {
        if (roleManager is RoleManagerWrapper<TRole> wrapper)
        {
            return wrapper.DisableInterceptors(names);
        }

        return roleManager;
    }
    
    public static RoleManager<TRole> DisableMultiTenancy<TRole>(this RoleManager<TRole> roleManager) where TRole : class
    {
        if (roleManager is RoleManagerWrapper<TRole> wrapper)
        {
            return wrapper
                .DisableQueryFilters("multi-tenancy")
                .DisableInterceptors("multi-tenancy");
        }

        return roleManager;
    }
    
    public static RoleManager<TRole> DisableSoftDelete<TRole>(this RoleManager<TRole> roleManager) where TRole : class
    {
        if (roleManager is RoleManagerWrapper<TRole> wrapper)
        {
            return wrapper
                .DisableQueryFilters("soft-delete")
                .DisableInterceptors("soft-delete");
        }

        return roleManager;
    }
    
}
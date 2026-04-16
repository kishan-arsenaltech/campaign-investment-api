using Investment.Core.Entities;
using System.Security.Claims;

namespace Investment.Authorization.Helper
{
    public static class PermissionHelper
    {
        public static bool HasPermission(ClaimsPrincipal user, string moduleName, string permission)
        {
            return user.Claims.Any(c =>
                c.Type == "Permission" &&
                c.Value == $"{moduleName}.{permission}");
        }

        public static bool IsSuperAdmin(ClaimsPrincipal user)
        {
            return user.Claims.Any(c =>
                c.Type == "IsSuperAdmin" &&
                c.Value == "True");
        }

        public static bool HasRole(ClaimsPrincipal user, string role)
        {
            return user.Claims.Any(c =>
                c.Type == ClaimTypes.Role &&
                c.Value == role);
        }

        public static bool IsAdmin(ClaimsPrincipal user)
        {
            return HasRole(user, UserRoles.Admin);
        }

        public static bool IsSuperAdminOrAdmin(ClaimsPrincipal user)
        {
            return IsSuperAdmin(user) || IsAdmin(user);
        }
    }
}

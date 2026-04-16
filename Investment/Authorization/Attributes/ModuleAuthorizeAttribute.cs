using Investment.Authorization.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Investment.Authorization.Attributes
{
    public class ModuleAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly PermissionType _permission;

        public ModuleAuthorizeAttribute(PermissionType permission)
        {
            _permission = permission;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var allowAnonymous = context.ActionDescriptor.EndpointMetadata.Any(em => em is AllowAnonymousAttribute);

            if (allowAnonymous)
                return;

            var user = context.HttpContext.User;

            if (!user.Identity!.IsAuthenticated)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var controllerActionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;

            var moduleAttribute = controllerActionDescriptor?
                .ControllerTypeInfo
                .GetCustomAttributes(typeof(ModuleAttribute), true)
                .FirstOrDefault() as ModuleAttribute;

            if (moduleAttribute == null)
                return;

            var module = moduleAttribute.Name;

            var permissions = user.Claims
                .Where(c => c.Type == "Permission")
                .Select(c => c.Value)
                .ToHashSet();

            var requiredPermission = $"{module.ToLower()}.{_permission}";

            if (!permissions.Contains(requiredPermission))
                context.Result = new ForbidResult();
        }
    }
}

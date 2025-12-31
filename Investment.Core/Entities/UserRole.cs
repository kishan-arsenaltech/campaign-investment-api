using Microsoft.AspNetCore.Identity;

namespace Investment.Core.Entities
{
    public class ApplicationUserRole : IdentityUserRole<string>
    {
        public override string UserId { get => base.UserId; set => base.UserId = value; }
        public override string RoleId { get => base.RoleId; set => base.RoleId = value; }
    }
}

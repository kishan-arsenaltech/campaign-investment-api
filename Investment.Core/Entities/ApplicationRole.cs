using Microsoft.AspNetCore.Identity;

namespace Invest.Core.Models
{
    public class ApplicationRole : IdentityRole
    {
        public bool IsSuperAdmin { get; set; } = false;
    }
}

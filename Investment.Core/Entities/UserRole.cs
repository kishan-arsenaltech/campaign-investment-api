using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class ApplicationUserRole : IdentityUserRole<string>, IBaseEntity
    {
        public override string UserId { get => base.UserId; set => base.UserId = value; }
        public override string RoleId { get => base.RoleId; set => base.RoleId = value; }

        public bool IsDeleted { get; set; } = false;
        public string? DeletedBy { get; set; }
        public User? DeletedByUser { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? DeletedAt { get; set; }
    }
}

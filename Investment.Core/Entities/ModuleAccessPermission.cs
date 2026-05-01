using Invest.Core.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class ModuleAccessPermission
    {
        public int Id { get; set; }

        public int ModuleId { get; set; }
        public Module? Module { get; set; }

        public string RoleId { get; set; } = string.Empty;
        public virtual ApplicationRole Role { get; set; } = null!;

        public bool Manage { get; set; } = false;
        public bool Delete { get; set; } = false;

        public string UpdatedBy { get; set; } = string.Empty;
        public User UpdatedByUser { get; set; } = null!;

        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? UpdatedAt { get; set; }
    }
}

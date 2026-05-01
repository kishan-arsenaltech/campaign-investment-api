using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public abstract class BaseEntity : IBaseEntity
    {
        public bool IsDeleted { get; set; } = false;

        public string? DeletedBy { get; set; }
        public User? DeletedByUser { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? DeletedAt { get; set; }
    }

    public interface IBaseEntity
    {
        bool IsDeleted { get; set; }
        string? DeletedBy { get; set; }
        User? DeletedByUser { get; set; }
        DateTime? DeletedAt { get; set; }
    }
}

using System.ComponentModel.DataAnnotations.Schema;

namespace Invest.Core.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public string? TableName { get; set; }
        public string? RecordId { get; set; }
        public string? ActionType { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? ChangedColumns { get; set; }
        public string? UpdatedBy { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}

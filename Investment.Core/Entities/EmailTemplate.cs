using Invest.Core.Models;
using Investment.Core.Constants;
using Investment.Core.Entities;
using System.ComponentModel.DataAnnotations.Schema;

namespace Invest.Core.Entities
{
    public class EmailTemplate : BaseEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public string BodyHtml { get; set; } = null!;
        public EmailTemplateCategory Category { get; set; }
        public EmailTemplateStatus Status { get; set; }
        public string? Receiver { get; set; }
        public string? TriggerAction { get; set; }

        public string? CreatedBy { get; set; }
        public User? CreatedByUser { get; set; }
        
        public string? ModifiedBy { get; set; }
        public User? ModifiedByUser { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? ModifiedAt { get; set; }

        public ICollection<EmailTemplateVariable>? Variables { get; set; }
    }
}

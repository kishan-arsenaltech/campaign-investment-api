// Ignore Spelling: Webhook

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Invest.Core.Entities
{
    public class UserStripeTransactionMapping
    {
        [Key]
        public Guid? Id { get; set; } = Guid.NewGuid();
        public Guid? UserId { get; set; }
        public string? TransactionId { get; set; }
        public string? Status { get; set; }
        public string? WebhookStatus { get; set; }
        public decimal? Amount { get; set; }
        public string? Country { get; set; }
        public string? ZipCode { get; set; }
        public string? RequestedData { get; set; }
        public string? ResponseData { get; set; }
        public string? WebhookResponseData { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? WebhookExecutionDate { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}

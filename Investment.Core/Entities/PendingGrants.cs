using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class PendingGrants
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;
        public string Amount { get; set; } = string.Empty;
        public decimal? GrantAmount { get; set; }
        public decimal? AmountAfterFees { get; set; }
        public string DAFProvider { get; set; } = string.Empty;
        public string? DAFName { get; set; }
        public int? CampaignId { get; set; }
        public CampaignDto? Campaign { get; set; }
        public string? InvestedSum { get; set; }
        public decimal? TotalInvestedAmount { get; set; }
        public string? status { get; set; }
        public string? Reference { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public string? RejectionMemo { get; set; }
        public string? RejectedBy { get; set; }
        public User? RejectedByUser { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? RejectionDate { get; set; }
    }
}

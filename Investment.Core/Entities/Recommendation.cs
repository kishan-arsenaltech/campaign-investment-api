using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class Recommendation
    {
        public int Id { get; set; }
        public string? UserId { get; set; }
        public User? User { get; set; }
        public string? UserEmail { get; set; }
        public string? UserFullName { get; set; }
        public int? CampaignId { get; set; }
        public CampaignDto? Campaign { get; set; }
        public string? Status { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? DateCreated { get; set; }
        public int? PendingGrantsId { get; set; }
        public PendingGrants? PendingGrants { get; set; }
        public string? RejectionMemo { get; set; }
        public string? RejectedBy { get; set; }
        public User? RejectedByUser { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? RejectionDate { get; set; }
    }
}

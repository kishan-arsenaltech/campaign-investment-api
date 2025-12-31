using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class ReturnMaster
    {
        public int Id { get; set; }
        public int CampaignId { get; set; }
        public CampaignDto? Campaign { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public User? CreatedByUser { get; set; }
        public decimal ReturnAmount { get; set; }
        public int? TotalInvestors { get; set; }
        public decimal TotalInvestmentAmount { get; set; }
        public string? MemoNote { get; set; }
        public string? Status { get; set; }

        [Column(TypeName = "date")]
        public DateTime? PrivateDebtStartDate { get; set; } = null;

        [Column(TypeName = "date")]
        public DateTime? PrivateDebtEndDate { get; set; } = null;

        [Column(TypeName = "date")]
        public DateTime PostDate { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public ICollection<ReturnDetails>? ReturnDetails { get; set; }
    }
}

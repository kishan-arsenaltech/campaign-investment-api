using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class CompletedInvestmentsDetails : BaseEntity
    {
        public int Id { get; set; }

        [Column(TypeName = "date")]
        public DateTime? DateOfLastInvestment { get; set; }

        public int CampaignId { get; set; }
        public CampaignDto? Campaign { get; set; }
        public string? InvestmentDetail { get; set; }
        public decimal? Amount { get; set; }
        public string? TypeOfInvestment { get; set; }
        public string? InvestmentVehicle { get; set; }
        public int? Donors { get; set; }
        public string? Themes { get; set; }
        public int? SiteConfigurationId { get; set; }
        public SiteConfiguration? SiteConfiguration { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public User? CreatedByUser { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedOn { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? ModifiedOn { get; set; }
    }
}

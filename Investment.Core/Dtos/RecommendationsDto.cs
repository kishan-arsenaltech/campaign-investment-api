namespace Investment.Core.Dtos
{
    public class RecommendationsDto
    {
        public int Id { get; set; }
        public string? UserEmail { get; set; }
        public string? UserFullName { get; set; }
        public int? CampaignId { get; set; }
        public string? CampaignName { get; set; }
        public string? Status { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? DateCreated { get; set; }
        public string? RejectionMemo { get; set; }
    }
}

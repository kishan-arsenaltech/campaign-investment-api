namespace Investment.Core.Dtos
{
    public class CompletedInvestmentsResponseDto
    {
        public DateTime? DateOfLastInvestment { get; set; }
        public string? TypeOfInvestmentIds { get; set; }
        public decimal? ApprovedRecommendationsAmount { get; set; }
        public decimal? PendingRecommendationsAmount { get; set; }
    }
}

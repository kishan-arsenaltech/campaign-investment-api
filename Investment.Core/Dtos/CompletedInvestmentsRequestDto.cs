namespace Investment.Core.Dtos
{
    public class CompletedInvestmentsRequestDto
    {
        public int InvestmentId { get; set; }
        public string? InvestmentDetail { get; set; }
        public decimal? TotalInvestmentAmount { get; set; }
        public DateTime? DateOfLastInvestment { get; set; }
        public string? TypeOfInvestmentIds { get; set; }
        public string? TypeOfInvestmentName { get; set; }
    }
}

namespace Investment.Core.Dtos
{
    public class InvestmentFeedbackDto
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Themes { get; set; }
        public string? AdditionalThemes { get; set; }
        public string? InterestedInvestmentType { get; set; }
        public int? RiskTolerance { get; set; }
    }

    public enum InterestedInvestmentType
    {
        EquityFund = 0,
        LoanFund = 1,
        DirectEquity = 2,
        DirectLoan = 3
    }
}

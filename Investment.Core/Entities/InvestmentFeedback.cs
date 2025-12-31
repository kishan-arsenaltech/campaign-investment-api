namespace Investment.Core.Entities
{
    public class InvestmentFeedback
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;
        public string? Themes { get; set; }
        public string? AdditionalThemes { get; set; }
        public string? InterestedInvestmentType { get; set; }
        public int? RiskTolerance { get; set; }
    }
}

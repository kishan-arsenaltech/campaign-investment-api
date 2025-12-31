namespace Investment.Core.Dtos
{
    public class ReturnsHistoryResponseDto
    {
        public string? InvestmentName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public decimal? InvestmentAmount { get; set; }
        public decimal? Percentage { get; set; }
        public decimal? ReturnedAmount { get; set; }
        public string? Memo { get; set; }
        public string? Status { get; set; }
        public string? PrivateDebtDates { get; set; }
        public string? PostDate { get; set; }
    }
}

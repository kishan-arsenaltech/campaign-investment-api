namespace Investment.Core.Dtos
{
    public class CompletedInvestmentsHistoryResponseDto
    {
        public DateTime? DateOfLastInvestment { get; set; }
        public string? InvestmentName { get; set; }
        public string? InvestmentDetail { get; set; }
        public decimal? TotalInvestmentAmount { get; set; }
        public string? TypeOfInvestment { get; set; }
        public int? Donors { get; set; }
        public string? Themes { get; set; }
        public string? Property { get; set; }
    }

    public class CompletedInvestmentsPaginationDto
    {
        public int? CurrentPage { get; set; }
        public int? PerPage { get; set; }
        public string? SortField { get; set; }
        public string? SortDirection { get; set; }
        public string? SearchValue { get; set; }
        public string? ThemesId { get; set; }
        public string? InvestmentTypeId { get; set; }
    }
}

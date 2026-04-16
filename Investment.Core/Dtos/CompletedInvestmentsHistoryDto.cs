namespace Investment.Core.Dtos
{
    public class CompletedInvestmentsHistoryResponseDto
    {
        public int? Id { get; set; }
        public DateTime? DateOfLastInvestment { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Target { get; set; }
        public decimal? CurrentBalance { get; set; }
        public int? NumberOfInvestors { get; set; }
        public string? Stage { get; set; }
        public string? TileImageFileName { get; set; }
        public string? CataCapFund { get; set; }
        public int? TransactionType { get; set; }
        public string? TransactionTypeValue { get; set; }
        public string? InvestmentDetail { get; set; }
        public decimal? TotalInvestmentAmount { get; set; }
        public string? TypeOfInvestment { get; set; }
        public int? Donors { get; set; }
        public string? Themes { get; set; }
        public string? Property { get; set; }
        public decimal ApprovedRecommendationsAmount { get; set; }
        public string? InvestmentVehicle { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
        public List<string>? LatestInvestorAvatars { get; set; }
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
        public bool? IsDeleted { get; set; }
    }
}

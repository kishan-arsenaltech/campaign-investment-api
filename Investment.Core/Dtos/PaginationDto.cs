namespace Investment.Core.Dtos
{
    public class PaginationDto
    {
        public int? CurrentPage { get; set; }
        public int? PerPage { get; set; }
        public string? SortField { get; set; }
        public string? SortDirection { get; set; }
        public string? SearchValue { get; set; }
        public string? Status { get; set; }
        public int? InvestmentId { get; set; }
        public bool? FilterByGroup { get; set; }
        public string? Stages { get; set; }
    }
}

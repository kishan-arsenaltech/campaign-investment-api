namespace Investment.Core.Dtos
{
    public class ReturnsHistoryRequestDto
    {
        public int InvestmentId { get; set; }
        public int? CurrentPage { get; set; }
        public int? PerPage { get; set; }
    }
}

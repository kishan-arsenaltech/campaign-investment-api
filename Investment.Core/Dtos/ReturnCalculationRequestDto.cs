namespace Investment.Core.Dtos
{
    public class ReturnCalculationRequestDto
    {
        public int InvestmentId { get; set; }
        public decimal ReturnAmount { get; set; }
        public string? MemoNote { get; set; }
        public int? CurrentPage { get; set; }
        public int? PerPage { get; set; }
        public DateTime? PrivateDebtStartDate { get; set; }
        public DateTime? PrivateDebtEndDate { get; set; }
    }
}

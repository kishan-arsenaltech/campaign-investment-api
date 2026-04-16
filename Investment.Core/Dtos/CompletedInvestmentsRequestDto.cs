namespace Investment.Core.Dtos
{
    public class CompletedInvestmentsRequestDto
    {
        public int? Id { get; set; }
        public int InvestmentId { get; set; }
        public string? InvestmentDetail { get; set; }
        public decimal? TotalInvestmentAmount { get; set; }
        public int? TransactionTypeId { get; set; }
        public DateTime? DateOfLastInvestment { get; set; }
        public string? TypeOfInvestmentIds { get; set; }
        public string? TypeOfInvestmentName { get; set; }
        public string? Note { get; set; }
        public string? InvestmentVehicle { get; set; }
    }
}

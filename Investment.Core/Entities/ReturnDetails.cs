namespace Investment.Core.Entities
{
    public class ReturnDetails
    {
        public int Id { get; set; }
        public int ReturnMasterId { get; set; }
        public ReturnMaster? ReturnMaster { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }
        public decimal InvestmentAmount { get; set; }
        public decimal PercentageOfTotalInvestment { get; set; }
        public decimal ReturnAmount { get; set; }
    }
}

using Investment.Core.Entities;

namespace Invest.Core.Entities
{
    public class AccountBalanceChangeLog
    {
        public int Id { get; set; }
        /// <summary>
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }
        public string? InvestmentName { get; set; }
        public string? PaymentType { get; set; }
        public decimal? OldValue { get; set; }
        public string UserName { get; set; } = string.Empty;
        public decimal? NewValue { get; set; }
        public int? GroupId { get; set; }
        public Group? Group { get; set; }
        public DateTime ChangeDate { get; set; } = DateTime.Now;
        public int? PendingGrantsId { get; set; }
        public PendingGrants? PendingGrants { get; set; }
        public string? TransactionStatus { get; set; }
        public string? Reference { get; set; }
    }
}

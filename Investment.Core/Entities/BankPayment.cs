namespace Invest.Core.Entities
{
    public class BankPayment
    {
        public bool IsAnonymous { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int? InvestmentId { get; set; }
        public string setup_intent { get; set; } = string.Empty;
        public string setup_intent_client_secret { get; set; } = string.Empty;
        public string redirect_status { get; set; } = string.Empty;
        public string? Reference { get; set; }
    }

    public class ACHPaymentSecret
    {
        public bool IsAnonymous { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public long Amount { get; set; }
    }
}

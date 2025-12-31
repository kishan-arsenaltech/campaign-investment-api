namespace Invest.Core.Entities
{
    public class CardPayment
    {
        public bool IsAnonymous { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string PaymentMethodId { get; set; } = string.Empty;
        public string TokenId { get; set; } = string.Empty;
        public long Amount { get; set; }
        public bool RememberCardDetail { get; set; }
        public int? InvestmentId { get; set; }
        public string? Reference { get; set; }
        public BillingDetails? BillingDetails { get; set; }
    }

    public class BillingDetails
    {
        public string? Country { get; set; }
        public string? ZipCode { get; set; }
    }

    public class PaymentMethodDetails
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Last4 { get; set; } = string.Empty;
        public long ExpiryMonth { get; set; }
        public long ExpiryYear { get; set; }
    }
}
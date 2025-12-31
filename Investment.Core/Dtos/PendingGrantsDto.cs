namespace Investment.Core.Dtos
{
    public class PendingGrantsDto
    {
        public bool IsAnonymous { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string DAFProvider { get; set; } = string.Empty;
        public string DAFName { get; set; } = string.Empty;
        public string InvestmentId { get; set; } = string.Empty;
        public decimal InvestedSum { get; set; }
        public string? Reference { get; set; }
    }
}

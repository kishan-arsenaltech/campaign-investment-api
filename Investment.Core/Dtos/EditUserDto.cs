namespace Investment.Core.Dtos
{
    public class EditUserDto
    {
        public string Token { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PictureFile { get; set; } = string.Empty;
        public string PictureFileName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public decimal AccountBalance { get; set; }
        public string UserName { get; set; } = string.Empty;
        public bool IsApprouveRequired { get; set; } = false;
        public bool IsUserHidden { get; set; } = false;
        public bool EmailFromGroupsOn { get; set; } = false;
        public bool EmailFromUsersOn { get; set; } = false;
        public bool OptOutEmailNotifications { get; set; } = false;
        public bool Feedback { get; set; } = false;
        public bool IsFreeUser { get; set; } = false;
        public bool IsAnonymousInvestment { get; set; } = false;
        public List<string>? GroupLinks { get; set; }
        public int? InvestmentId { get; set; }
    }
}

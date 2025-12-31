// Ignore Spelling: Dtos Dto Approuve Daf

namespace Invest.Core.Dtos
{
    public class UserDetailsDto
    {
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PictureFileName { get; set; }
        public string? Address { get; set; }
        public decimal AccountBalance { get; set; }
        public string? UserName { get; set; }
        public bool IsApprouveRequired { get; set; }
        public bool IsUserHidden { get; set; }
        public bool EmailFromGroupsOn { get; set; }
        public bool EmailFromUsersOn { get; set; }
        public bool OptOutEmailNotifications { get; set; }
        public bool? IsFreeUser { get; set; }
        public bool? IsAnonymousInvestment { get; set; }
    }
}

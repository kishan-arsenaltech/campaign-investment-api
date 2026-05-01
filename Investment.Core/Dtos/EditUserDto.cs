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
        public bool IsApprouveRequired { get; set; }
        public bool IsUserHidden { get; set; }
        public bool EmailFromGroupsOn { get; set; }
        public bool EmailFromUsersOn { get; set; }
        public bool OptOutEmailNotifications { get; set; }
        public bool Feedback { get; set; }
        public bool? IsFreeUser { get; set; }
        public bool? IsAnonymousInvestment { get; set; }
        public bool ConsentToShowAvatar { get; set; }
        public List<string>? GroupLinks { get; set; }
        public bool HasInvestments { get; set; }
        public string? ZipCode { get; set; }
    }
}

namespace Investment.Core.Dtos
{
    public class UsersExportDto
    {
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string IsActive { get; set; } = string.Empty;
        public int Recommendations { get; set; }
        public string AmountInvested { get; set; } = string.Empty;
        public string AmountInAccount { get; set; } = string.Empty;
        public string FollowingGroups { get; set; } = string.Empty;
        public string GroupOwner { get; set; } = string.Empty;
        public string? IsGroupAdmin { get; set; }
        public string? ZipCode { get; set; }
        public string? IsExcludeUserBalance { get; set; }

        public string SurveyThemes { get; set; } = string.Empty;
        public string? SurveyAdditionalThemes { get; set; }
        public string SurveyInvestmentInterest { get; set; } = string.Empty;
        public string SurveyRiskTolerance { get; set; } = string.Empty;
        public DateTime? DateCreated { get; set; }
    }
}

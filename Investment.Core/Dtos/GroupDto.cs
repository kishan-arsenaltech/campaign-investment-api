using Investment.Core.Entities;

namespace Investment.Core.Dtos
{
    public class GroupDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PictureFileName { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal? OriginalBalance { get; set; }
        public decimal? CurrentBalance { get; set; }
        public bool IsApprouveRequired { get; set; }
        public bool IsDeactivated { get; set; }
        public string Token { get; set; } = string.Empty;
        public bool IsOwner { get; set; }
        public bool IsFollowing { get; set; }
        public bool IsFollowPending { get; set; }
        public string? Identifier { get; set; }
        public bool IsLeader { get; set; }
        public bool IsCorporateGroup { get; set; } = false;
        public bool IsPrivateGroup { get; set; } = false;
        public GroupAccountBalanceDto groupAccountBalance { get; set; } = new GroupAccountBalanceDto();
        public List<Campaign>? Campaigns { get; set; }
        public List<Campaign>? PrivateCampaigns { get; set; }
    }
}

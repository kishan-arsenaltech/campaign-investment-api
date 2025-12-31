namespace Investment.Core.Entities
{
    public class Group
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? PictureFileName { get; set; }
        public string? Website { get; set; }
        public string? Description { get; set; }
        public bool? IsApprouveRequired { get; set; }
        public bool IsDeactivated { get; set; }
        public string? Identifier { get; set; }
        public decimal? OriginalBalance { get; set; }
        public bool IsCorporateGroup { get; set; } = false;
        public bool IsPrivateGroup { get; set; } = false;
        public User? Owner { get; set; }
        public List<CampaignDto>? Campaigns { get; set; } = new();
        public List<CampaignDto>? PrivateCampaigns { get; set; }

        public ICollection<FollowingRequest>? Requests { get; set; }
        public ICollection<LeaderGroup> LeadersGroup { get; set; } = new List<LeaderGroup>();
    }
}

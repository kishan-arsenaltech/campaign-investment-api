using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class Group : BaseEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? PictureFileName { get; set; }
        public string? BackgroundPictureFileName { get; set; }
        public string? Website { get; set; }
        public string? Description { get; set; }
        public bool? IsApprouveRequired { get; set; }
        public bool IsDeactivated { get; set; }
        public string? Identifier { get; set; }
        public string? VideoLink { get; set; }
        public string? OurWhyDescription { get; set; }
        public string? DidYouKnow { get; set; }
        public decimal? OriginalBalance { get; set; }
        public bool IsCorporateGroup { get; set; } = false;
        public bool IsPrivateGroup { get; set; } = false;
        public User? Owner { get; set; }
        public string? Leaders { get; set; }
        public string? ChampionsAndCatalysts { get; set; }
        public bool FeaturedGroup { get; set; } = false;
        public string? GroupThemes { get; set; }
        public string? MetaTitle { get; set; }
        public string? MetaDescription { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? CreatedAt { get; set; } = DateTime.Now;

        [Column(TypeName = "datetime")]
        public DateTime? ModifiedAt { get; set; }

        public List<CampaignDto>? Campaigns { get; set; } = new();
        public List<CampaignDto>? PrivateCampaigns { get; set; }

        public ICollection<FollowingRequest>? Requests { get; set; }
        public ICollection<LeaderGroup>? LeadersGroup { get; set; }
    }
}

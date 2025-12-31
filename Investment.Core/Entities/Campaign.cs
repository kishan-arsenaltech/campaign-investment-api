using Investment.Core.Dtos;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Investment.Core.Entities
{
    public class CampaignDto
    {
        public int? Id { get; set; }

        [MaxLength(100, ErrorMessage = "Maximum length for the Name is 100 characters.")]
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Themes { get; set; }
        public string? ApprovedBy { get; set; }
        public string? SDGs { get; set; }
        public string? InvestmentTypes { get; set; }
        public string? Terms { get; set; }
        public string? MinimumInvestment { get; set; }
        public string? Website { get; set; }
        public string? NetworkDescription { get; set; }
        public string? ContactInfoFullName { get; set; }
        public string? ContactInfoAddress { get; set; }
        public string? ContactInfoAddress2 { get; set; }
        public string? ContactInfoEmailAddress { get; set; }
        public string? InvestmentInformationalEmail { get; set; }
        public string? ContactInfoPhoneNumber { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? ImpactAssetsFundingStatus { get; set; }
        public string? InvestmentRole { get; set; }
        public string? Referred { get; set; }
        public string? UserId { get; set; }
        public User? User { get; set; }
        public string? Target { get; set; }
        public string? Status { get; set; }
        public string? TileImageFileName { get; set; }
        public string? ImageFileName { get; set; }
        public string? PdfFileName { get; set; }
        public string? OriginalPdfFileName { get; set; }
        public string? LogoFileName { get; set; }
        public bool? IsActive { get; set; } = false;
        public InvestmentStage? Stage { get; set; }
        public string? Property { get; set; }
        public int? AddedTotalAdminRaised { get; set; }
        public List<Group> Groups { get; set; } = new();
        public ICollection<Recommendation>? Recommendations { get; set; }
        public int? GroupForPrivateAccessId { get; set; }
        public Group? GroupForPrivateAccess { get; set; }
        public bool? EmailSends { get; set; }
        public string? FundraisingCloseDate { get; set; }
        public string? MissionAndVision { get; set; }
        public string? PersonalizedThankYou { get; set; }
        public bool? HasExistingInvestors { get; set; }
        public decimal? ExpectedTotal { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    public class Campaign
    {
        public int? Id { get; set; }

        [MaxLength(100, ErrorMessage = "Maximum length for the Name is 100 characters.")]
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Themes { get; set; }
        public string? ApprovedBy { get; set; }
        public string? SDGs { get; set; }
        public string? Image { get; set; }
        public string? TileImage { get; set; }
        public string? InvestmentTypes { get; set; }
        public string? Logo { get; set; }
        public string? PDFPresentation { get; set; }
        public string? Terms { get; set; }
        public string? MinimumInvestment { get; set; }
        public string? Website { get; set; }
        public string? NetworkDescription { get; set; }
        public string? ContactInfoFullName { get; set; }
        public string? ContactInfoAddress { get; set; }
        public string? ContactInfoAddress2 { get; set; }
        public string? ContactInfoEmailAddress { get; set; }
        public string? InvestmentInformationalEmail { get; set; }
        public string? ContactInfoPhoneNumber { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? ImpactAssetsFundingStatus { get; set; }
        public string? InvestmentRole { get; set; }
        public string? Referred { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Status { get; set; }
        public string? Target { get; set; }
        public string? TileImageFileName { get; set; }
        public string? ImageFileName { get; set; }
        public string? PdfFileName { get; set; }
        public string? OriginalPdfFileName { get; set; }
        public string? LogoFileName { get; set; }
        public bool? IsActive { get; set; }
        public InvestmentStage? Stage { get; set; }
        public string? Property { get; set; }
        public int? AddedTotalAdminRaised { get; set; }
        public decimal? CurrentBalance { get; set; }
        public int? NumberOfInvestors { get; set; }
        public GroupDto? GroupForPrivateAccessDto { get; set; }
        public bool? EmailSends { get; set; }
        public string? FundraisingCloseDate { get; set; }
        public string? MissionAndVision { get; set; }
        public string? PersonalizedThankYou { get; set; }
        public bool? HasExistingInvestors { get; set; }
        public decimal? ExpectedTotal { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public List<MatchedCampaignsCardDto>? MatchedCampaigns { get; set; }
        public string? CaptchaToken { get; set; }
    }

    public class CampaignCardDto
    {
        public int? Id { get; set; }

        [MaxLength(100, ErrorMessage = "Maximum length for the Name is 100 characters.")]
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public string? Target { get; set; }
        public string TileImageFileName { get; set; } = string.Empty;
        public string ImageFileName { get; set; } = string.Empty;
        public string? Property { get; set; }
        public string? Themes { get; set; }
        public string? InvestmentTypes { get; set; } = null;
        public decimal? CurrentBalance { get; set; }
        public int? NumberOfInvestors { get; set; }
        public GroupDto? GroupForPrivateAccessDto { get; set; }
    }

    public class MatchedCampaignsCardDto
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Target { get; set; }
        public string? TileImageFileName { get; set; }
        public string? ImageFileName { get; set; }
        public string? Property { get; set; }
        public decimal? CurrentBalance { get; set; }
        public int? NumberOfInvestors { get; set; }
        public List<string>? LatestInvestorAvatars { get; set; }
    }

    public class CampaignCardWithCategories
    {
        public IEnumerable<CampaignCardDto> Campaigns { get; set; } = Enumerable.Empty<CampaignCardDto>();
        public IEnumerable<CategoryDto> Categories { get; set; } = Enumerable.Empty<CategoryDto>();
        public IEnumerable<InvestmentType> InvestmentTypes { get; set; } = Enumerable.Empty<InvestmentType>();
    }
}

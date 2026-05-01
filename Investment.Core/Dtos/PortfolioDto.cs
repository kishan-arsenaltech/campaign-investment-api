using Investment.Core.Entities;

namespace Investment.Core.Dtos
{
    public class PortfolioDto
    {
        public decimal? AccountBalance { get; set; }
        public decimal? GroupBalance { get; set; }
        public List<RecommendationsDto> Recommendations { get; set; } = new List<RecommendationsDto>();
        public List<Campaign> Campaigns { get; set; } = new List<Campaign>();
    }
}

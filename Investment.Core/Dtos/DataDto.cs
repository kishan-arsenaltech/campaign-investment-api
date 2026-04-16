using Invest.Core.Entities;
using Investment.Core.Entities;

namespace Investment.Core.Dtos
{
    public class DataDto
    {
        public IEnumerable<Theme> Theme { get; set; } = Enumerable.Empty<Theme>();
        public IEnumerable<Sdg> Sdg { get; set; } = Enumerable.Empty<Sdg>();
        public IEnumerable<InvestmentType> InvestmentType { get; set; } = Enumerable.Empty<InvestmentType>();
        public IEnumerable<ApprovedBy> ApprovedBy { get; set; } = Enumerable.Empty<ApprovedBy>();
    }
}

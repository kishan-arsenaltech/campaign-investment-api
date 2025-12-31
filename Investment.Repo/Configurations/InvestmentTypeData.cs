using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Invest.Repo.Configurations;

public class InvestmentTypeData : IEntityTypeConfiguration<InvestmentType>
{
    public void Configure(EntityTypeBuilder<InvestmentType> builder)
    {
        builder.HasData(
            new InvestmentType
            {
                Id = 1,
                Name = "Venture Capital",
            },
            new InvestmentType
            {
                Id = 2,
                Name = "Private Equity",
            },
            new InvestmentType
            {
                Id = 3,
                Name = "Private Debt"
            },
            new InvestmentType
            {
                Id = 4,
                Name = "Small Business Lending",
            },
            new InvestmentType
            {
                Id = 5,
                Name = "Microfinance",
            },
            new InvestmentType
            {
                Id = 6,
                Name = "Project Finance"
            },
            new InvestmentType
            {
                Id = 7,
                Name = "Cash Equivalents",
            },
            new InvestmentType
            {
                Id = 8,
                Name = "Emerging Markets",
            },
            new InvestmentType
            {
                Id = 9,
                Name = "International Investing"
            },
            new InvestmentType
            {
                Id = 10,
                Name = "Real Assets"
            },
            new InvestmentType
            {
                Id = 11,
                Name = "Real Estate"
            });
    }
}

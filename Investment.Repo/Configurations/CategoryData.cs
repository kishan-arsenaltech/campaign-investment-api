using Invest.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class ApprovedByData : IEntityTypeConfiguration<ApprovedBy>
    {   
        public void Configure(EntityTypeBuilder<ApprovedBy> builder)
        {
            builder.HasData(
                new ApprovedBy
                {
                    Id = 1,
                    Name = "Impact Assets"
                },
                new ApprovedBy
                {
                    Id = 2,
                    Name = "Toniic Investors"
                });
        }
    }
}

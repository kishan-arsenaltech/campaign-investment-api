using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class SdgData : IEntityTypeConfiguration<Sdg>
    {
        public void Configure(EntityTypeBuilder<Sdg> builder)
        {
            builder.HasData(
                new Sdg
                {
                    Id = 1,
                    Name = "No poverty",
                },
                new Sdg
                {
                    Id = 2,
                    Name = "Zero hunger",
                },
                new Sdg
                {
                    Id = 3,
                    Name = "Good health and well-being"
                },
                new Sdg
                {
                    Id = 4,
                    Name = "Quality Education",
                },
                new Sdg
                {
                    Id = 5,
                    Name = "Gender equality",
                },
                new Sdg
                {
                    Id = 6,
                    Name = "Clean water and sanitation",
                },
                new Sdg
                {
                    Id = 7,
                    Name = "Affordable and clean energy",
                },
                new Sdg
                {
                    Id = 8,
                    Name = "Decent work and economic growth",
                },
                new Sdg
                {
                    Id = 9,
                    Name = "Industry, innovation and infrastructure",
                },
                new Sdg
                {
                    Id = 10,
                    Name = "Reduced inequalities",
                },
                new Sdg
                {
                    Id = 11,
                    Name = "Sustainable cities and economies",
                },
                new Sdg
                {
                    Id = 12,
                    Name = "Responsible consumption and production",
                },
                new Sdg
                {
                    Id = 13,
                    Name = "Climate action",
                },
                new Sdg
                {
                    Id = 14,
                    Name = "Life below water",
                },
                new Sdg
                {
                    Id = 15,
                    Name = "Life on land",
                },
                new Sdg
                {
                    Id = 16,
                    Name = "Peace, justice and strong institutions",
                },
                new Sdg
                {
                    Id = 17,
                    Name = "Partnership for the goals",
                });
        }
    }
}

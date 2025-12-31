using Invest.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Investment.Repo.Configurations
{
    public class CategoryData : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.HasData(
                new Category
                {
                    Id = 1,
                    Name = "Climate",
                    Mandatory = true
                },
                new Category
                {
                    Id = 2,
                    Name = "Gender",
                    Mandatory = true
                },
                new Category
                {
                    Id = 3,
                    Name = "Racial",
                    Mandatory = true
                },
                new Category
                {
                    Id = 4,
                    Name = "Poverty",
                    Mandatory = true
                });
        }
    }
}

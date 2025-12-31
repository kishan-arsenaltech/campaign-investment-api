using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class IdentityRoleData : IEntityTypeConfiguration<IdentityRole>
    {
        public void Configure(EntityTypeBuilder<IdentityRole> builder)
        {
            builder.HasData(
               new IdentityRole
               {
                   ConcurrencyStamp = "d06257e2-4ea8-4931-97f1-af0694b02c4d",
                   Id = "460da70e-6557-4584-8fe2-03524ea7f5dc",
                   Name = "Admin",
                   NormalizedName = "ADMIN"
               });
        }
    }
}

using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class ApplicationUserRoleData : IEntityTypeConfiguration<ApplicationUserRole>
    {
        public void Configure(EntityTypeBuilder<ApplicationUserRole> builder)
        {
            builder.HasData(
               new ApplicationUserRole
               {
                   UserId = "ccc24a6d-aa14-4f0e-ac7f-09740cb196f8",
                   RoleId = "460da70e-6557-4584-8fe2-03524ea7f5dc"
               });
        }
    }
}

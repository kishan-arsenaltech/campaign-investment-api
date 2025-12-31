using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class CampaignData : IEntityTypeConfiguration<CampaignDto>
    {
        public void Configure(EntityTypeBuilder<CampaignDto> builder)
        {
            builder.HasKey(x => x.Id);
            builder.HasOne(i => i.GroupForPrivateAccess).WithMany(i => i.PrivateCampaigns).HasForeignKey(i => i.GroupForPrivateAccessId);
            builder.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId);
        }
    }
}

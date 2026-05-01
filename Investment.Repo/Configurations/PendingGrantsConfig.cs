using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class PendingGrantsConfig : IEntityTypeConfiguration<PendingGrants>
    {
        public void Configure(EntityTypeBuilder<PendingGrants> builder)
        {
            builder.HasKey(d => d.Id);
            builder.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(d => d.Campaign).WithMany().HasForeignKey(d => d.CampaignId);
            builder.HasOne(x => x.RejectedByUser).WithMany().HasForeignKey(x => x.RejectedBy);
            builder.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedBy);
        }
    }
}

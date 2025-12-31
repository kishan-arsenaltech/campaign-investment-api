using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class RecommendationConfig : IEntityTypeConfiguration<Recommendation>
    {
        public void Configure(EntityTypeBuilder<Recommendation> builder)
        {
            builder.HasKey(d => d.Id);
            builder.HasOne(r => r.Campaign).WithMany(c => c.Recommendations).HasForeignKey(r => r.CampaignId);
            builder.HasOne(r => r.PendingGrants).WithMany().HasForeignKey(r => r.PendingGrantsId);
            builder.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId);
            builder.HasOne(x => x.RejectedByUser).WithMany().HasForeignKey(x => x.RejectedBy);
        }
    }
}

using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class CompletedInvestmentsDetailsConfig : IEntityTypeConfiguration<CompletedInvestmentsDetails>
    {
        public void Configure(EntityTypeBuilder<CompletedInvestmentsDetails> builder)
        {
            builder.HasKey(x => x.Id);
            builder.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId);
            builder.HasOne(x => x.SiteConfiguration).WithMany().HasForeignKey(x => x.SiteConfigurationId);
            builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy);
            builder.Property(x => x.DateOfLastInvestment).HasColumnType("date").IsRequired();
            builder.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedBy);
        }
    }
}

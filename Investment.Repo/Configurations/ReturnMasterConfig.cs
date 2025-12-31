using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class ReturnMasterConfig : IEntityTypeConfiguration<ReturnMaster>
    {
        public void Configure(EntityTypeBuilder<ReturnMaster> builder)
        {
            builder.HasKey(d => d.Id);
            builder.HasOne(i => i.Campaign).WithMany().HasForeignKey(i => i.CampaignId);
            builder.HasOne(d => d.CreatedByUser).WithMany().HasForeignKey(d => d.CreatedBy);
            builder.Property(r => r.PrivateDebtStartDate).HasColumnType("date").IsRequired(false);
            builder.Property(r => r.PrivateDebtEndDate).HasColumnType("date").IsRequired(false);
            builder.Property(r => r.PostDate).HasColumnType("date").IsRequired();
        }
    }
}

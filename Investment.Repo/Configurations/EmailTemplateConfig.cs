using Invest.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class EmailTemplateConfig : IEntityTypeConfiguration<EmailTemplate>
    {
        public void Configure(EntityTypeBuilder<EmailTemplate> builder)
        {
            builder.HasKey(d => d.Id);
            builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy);
            builder.HasOne(x => x.ModifiedByUser).WithMany().HasForeignKey(x => x.ModifiedBy);
            builder.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedBy);
        }
    }
}

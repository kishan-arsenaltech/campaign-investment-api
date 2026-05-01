using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class ReturnDetailsConfig : IEntityTypeConfiguration<ReturnDetails>
    {
        public void Configure(EntityTypeBuilder<ReturnDetails> builder)
        {
            builder.HasKey(d => d.Id);
            builder.HasOne(d => d.ReturnMaster).WithMany(r => r.ReturnDetails).HasForeignKey(d => d.ReturnMasterId);
            builder.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedBy);
        }
    }
}

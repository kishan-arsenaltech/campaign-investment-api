using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class ScheduledEmailLogsConfig : IEntityTypeConfiguration<ScheduledEmailLog>
    {
        public void Configure(EntityTypeBuilder<ScheduledEmailLog> builder)
        {
            builder.HasKey(x => x.Id);
            builder.HasOne(x => x.PendingGrants).WithMany().HasForeignKey(x => x.PendingGrantId);
            builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        }
    }
}

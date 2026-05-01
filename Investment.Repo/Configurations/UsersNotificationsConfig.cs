using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class UsersNotificationsConfig : IEntityTypeConfiguration<UsersNotification>
    {
        public void Configure(EntityTypeBuilder<UsersNotification> builder)
        {
            builder.HasOne(i => i.TargetUser).WithMany(i => i.Notifications);
            builder.HasOne(x => x.DeletedByUser).WithMany().HasForeignKey(x => x.DeletedBy);
        }
    }
}

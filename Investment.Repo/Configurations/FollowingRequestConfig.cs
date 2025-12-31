using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class FollowingRequestConfig : IEntityTypeConfiguration<FollowingRequest>
    {
        public void Configure(EntityTypeBuilder<FollowingRequest> builder)
        {
            builder.HasOne(i => i.RequestOwner).WithMany(i => i.Requests);
            builder.HasOne(i => i.UserToFollow).WithMany(i => i.RequestsToAccept);
            builder.HasOne(i => i.GroupToFollow).WithMany(i => i.Requests);
        }
    }
}

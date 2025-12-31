using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class GroupConfig : IEntityTypeConfiguration<Group>
    {
        public void Configure(EntityTypeBuilder<Group> builder)
        {
            builder.HasOne(i => i.Owner).WithMany(i => i.Groups);
            builder.HasMany(i => i.Campaigns).WithMany(i => i.Groups);
        }
    }
}

using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Investment.Repo.Configurations
{
    public class UserData : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasData(
               new User
               {
                   Id = "ccc24a6d-aa14-4f0e-ac7f-09740cb196f8",
                   Email = "admin@aa.com",
                   FirstName = "admin",
                   LastName = "admin",
                   UserName = "admin1",
                   NormalizedUserName = "ADMIN1",
                   PasswordHash = "AQAAAAEAACcQAAAAEKGOT9qPv6LK4JX2BuTAWMqWOyxMgY/5xa01QqSA8c0KfDDNRuqq9HLiwae1XKnnYQ==",
                   SecurityStamp = "KLLPL3UL2TBHSLYEJXDGXDUVFCVFFRZ",
                   ConcurrencyStamp = "bde4dffa-8ac6-42c9-82e9-d2eawer47a",
                   LockoutEnabled = true
               });
        }
    }
}

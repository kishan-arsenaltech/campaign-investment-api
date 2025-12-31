using Invest.Core.Entities;
using Invest.Repo.Configurations;
using Investment.Core.Entities;
using Investment.Repo.Configurations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Investment.Repo.Context
{
    public class RepositoryContext : IdentityDbContext<User>
    {
        public RepositoryContext(DbContextOptions<RepositoryContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new InvestmentTypeData());
            modelBuilder.ApplyConfiguration(new SdgData());
            modelBuilder.ApplyConfiguration(new CategoryData());
            modelBuilder.ApplyConfiguration(new CampaignData());
            modelBuilder.ApplyConfiguration(new IdentityRoleData());
            modelBuilder.ApplyConfiguration(new UserData());
            modelBuilder.ApplyConfiguration(new ApplicationUserRoleData());
            modelBuilder.ApplyConfiguration(new RecommendationConfig());
            modelBuilder.ApplyConfiguration(new GroupConfig());
            modelBuilder.ApplyConfiguration(new FollowingRequestConfig());
            modelBuilder.ApplyConfiguration(new UsersNotificationsConfig());
            modelBuilder.ApplyConfiguration(new ApprovedByData());
            modelBuilder.ApplyConfiguration(new ReturnMasterConfig());
            modelBuilder.ApplyConfiguration(new ReturnDetailsConfig());
            modelBuilder.ApplyConfiguration(new CompletedInvestmentsDetailsConfig());
            modelBuilder.ApplyConfiguration(new PendingGrantsConfig());
            modelBuilder.ApplyConfiguration(new ScheduledEmailLogsConfig());
        }
        public DbSet<InvestmentType> InvestmentTypes { get; set; } = null!;
        public DbSet<Sdg> SDGs { get; set; } = null!;
        public DbSet<Category> Themes { get; set; } = null!;
        public DbSet<CampaignDto> Campaigns { get; set; } = null!;
        public DbSet<Recommendation> Recommendations { get; set; } = null!;
        public DbSet<InvestmentFeedback> InvestmentFeedback { get; set; } = null!;
        public DbSet<AccountBalanceChangeLog> AccountBalanceChangeLogs { get; set; } = null!;
        public DbSet<PendingGrants> PendingGrants { get; set; } = null!;
        public DbSet<FollowingRequest> Requests { get; set; } = null!;
        public DbSet<Group> Groups { get; set; } = null!;
        public DbSet<UsersNotification> UsersNotifications { get; set; } = null!;
        public DbSet<ApprovedBy> ApprovedBy { get; set; } = null!;
        public DbSet<GroupAccountBalance> GroupAccountBalance { get; set; } = null!;
        public DbSet<UserStripeCustomerMapping> UserStripeCustomerMapping { get; set; } = null!;
        public DbSet<UserStripeTransactionMapping> UserStripeTransactionMapping { get; set; } = null!;
        public DbSet<ReturnMaster> ReturnMasters { get; set; } = null!;
        public DbSet<ReturnDetails> ReturnDetails { get; set; } = null!;
        public DbSet<CompletedInvestmentsDetails> CompletedInvestmentsDetails { get; set; } = null!;
        public DbSet<ScheduledEmailLog> ScheduledEmailLogs { get; set; } = null!;
        public DbSet<SchedulerLogs> SchedulerLogs { get; set; } = null!;
        public DbSet<DAFProviders> DAFProviders { get; set; } = null!;
    }
}

using Invest.Core.Entities;
using Invest.Core.Models;
using Invest.Repo.Configurations;
using Investment.Core.Entities;
using Investment.Repo.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Investment.Repo.Context
{
    public class RepositoryContext : IdentityDbContext<User, ApplicationRole, string>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RepositoryContext(DbContextOptions options, IHttpContextAccessor httpContextAccessor) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetPrecision(18);
                property.SetScale(2);
            }

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                bool isBaseEntity = typeof(BaseEntity).IsAssignableFrom(entityType.ClrType);
                bool isIBaseEntity = typeof(IBaseEntity).IsAssignableFrom(entityType.ClrType);

                if (isBaseEntity || isIBaseEntity)
                {
                    var pk = entityType.FindPrimaryKey();
                    if (pk == null || pk.Properties.Count > 1)
                        continue;

                    var filterMethod = typeof(RepositoryContext)
                        .GetMethod(nameof(SetSoftDeleteFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                        .MakeGenericMethod(entityType.ClrType);
                    filterMethod.Invoke(null, [modelBuilder]);

                    var configMethod = typeof(RepositoryContext)
                        .GetMethod(nameof(ConfigureBaseEntityRelationship), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                        .MakeGenericMethod(entityType.ClrType);
                    configMethod.Invoke(null, [modelBuilder]);
                }
            }

            modelBuilder.ApplyConfiguration(new InvestmentTypeData());
            modelBuilder.ApplyConfiguration(new SdgData());
            modelBuilder.ApplyConfiguration(new ThemeData());
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
            modelBuilder.ApplyConfiguration(new EmailTemplateConfig());
            modelBuilder.ApplyConfiguration(new ModuleAccessPermissionConfig());
            modelBuilder.ApplyConfiguration(new SiteConfigurationConfig());
        }

        private static void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IBaseEntity
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
        }

        private static void ConfigureBaseEntityRelationship<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IBaseEntity
        {
            modelBuilder.Entity<TEntity>()
                .HasOne(x => x.DeletedByUser)
                .WithMany()
                .HasForeignKey(x => x.DeletedBy)
                .OnDelete(DeleteBehavior.Restrict);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ChangeTracker.DetectChanges();

            var userId = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("id")?.Value;

            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Deleted)
                    continue;

                if (entry.Entity is IBaseEntity entity)
                {
                    var primaryKey = entry.Metadata.FindPrimaryKey();
                    if (primaryKey == null || primaryKey.Properties.Count > 1)
                        continue;

                    entry.State = EntityState.Modified;
                    entity.IsDeleted = true;
                    entity.DeletedAt = DateTime.Now;
                    entity.DeletedBy = userId;
                }
            }

            var auditLogs = new List<AuditLog>();

            var addedEntries = ChangeTracker.Entries()
                                            .Where(e =>
                                                e.State == EntityState.Added &&
                                                !(e.Entity is AuditLog) &&
                                                (e.Entity is CampaignDto || e.Entity is User || e.Entity is Group))
                                            .ToList();

            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.Entity is AuditLog ||
                    entry.State == EntityState.Detached ||
                    entry.State == EntityState.Unchanged ||
                    entry.State == EntityState.Added)
                    continue;

                if (!(entry.Entity is CampaignDto || entry.Entity is User || entry.Entity is Group))
                    continue;

                var tableName = entry.Metadata.GetTableName();

                var oldValues = new Dictionary<string, object?>();
                var newValues = new Dictionary<string, object?>();
                var changedColumns = new List<string>();

                foreach (var property in entry.Properties)
                {
                    var propertyName = property.Metadata.Name;

                    if (property.Metadata.IsPrimaryKey())
                        continue;

                    if (entry.State == EntityState.Deleted)
                    {
                        oldValues[propertyName] = property.OriginalValue;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        var original = property.OriginalValue;
                        var current = property.CurrentValue;

                        if (!Equals(original, current))
                        {
                            changedColumns.Add(propertyName);
                            oldValues[propertyName] = original;
                            newValues[propertyName] = current;
                        }
                    }
                }

                if (oldValues.Count == 0 && newValues.Count == 0)
                    continue;

                var primaryKey = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
                var recordId = primaryKey?.CurrentValue?.ToString();

                auditLogs.Add(new AuditLog
                {
                    TableName = tableName,
                    RecordId = recordId,
                    ActionType = entry.State.ToString(),
                    OldValues = oldValues.Count == 0 ? null : JsonSerializer.Serialize(oldValues),
                    NewValues = newValues.Count == 0 ? null : JsonSerializer.Serialize(newValues),
                    ChangedColumns = changedColumns.Count == 0 ? null : JsonSerializer.Serialize(changedColumns),
                    UpdatedBy = userId,
                    UpdatedAt = DateTime.Now
                });
            }

            var result = await base.SaveChangesAsync(cancellationToken);

            foreach (var entry in addedEntries)
            {
                var tableName = entry.Metadata.GetTableName();

                var newValues = new Dictionary<string, object?>();

                foreach (var property in entry.Properties)
                {
                    var propertyName = property.Metadata.Name;

                    if (property.Metadata.IsPrimaryKey())
                        continue;

                    newValues[propertyName] = property.CurrentValue;
                }

                if (newValues.Count == 0)
                    continue;

                var primaryKey = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
                var recordId = primaryKey?.CurrentValue?.ToString();

                auditLogs.Add(new AuditLog
                {
                    TableName = tableName,
                    RecordId = recordId,
                    ActionType = "Added",
                    OldValues = null,
                    NewValues = JsonSerializer.Serialize(newValues),
                    ChangedColumns = null,
                    UpdatedBy = userId,
                    UpdatedAt = DateTime.Now
                });
            }

            if (auditLogs.Any())
            {
                AuditLogs.AddRange(auditLogs);
                await base.SaveChangesAsync(cancellationToken);
            }

            return result;
        }

        public DbSet<InvestmentType> InvestmentTypes { get; set; } = null!;
        public DbSet<Sdg> SDGs { get; set; } = null!;
        public DbSet<Theme> Themes { get; set; } = null!;
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
        public DbSet<ApiErrorLog> ApiErrorLog { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<EmailTemplate> EmailTemplate { get; set; } = null!;
        public DbSet<EmailTemplateVariable> EmailTemplateVariable { get; set; } = null!;
        public DbSet<Module> Module { get; set; } = null!;
        public DbSet<ModuleAccessPermission> ModuleAccessPermission { get; set; } = null!;
        public DbSet<Slug> Slug { get; set; }
        public DbSet<SiteConfiguration> SiteConfiguration { get; set; }
    }
}

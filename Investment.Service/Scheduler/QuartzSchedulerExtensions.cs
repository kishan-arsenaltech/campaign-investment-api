using Investment.Service.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Investment.Service.Scheduler
{
    public static class QuartzSchedulerExtensions
    {
        public static void AddQuartzScheduler(this IServiceCollection services)
        {
            services.AddQuartz(q =>
            {
                var emailJobKey = new JobKey("SendReminderEmail");
                q.AddJob<SendReminderEmail>(opts => opts
                    .WithIdentity(emailJobKey)
                    .DisallowConcurrentExecution()
                );

                q.AddTrigger(opts => opts
                    .ForJob(emailJobKey)
                    .WithIdentity("SendReminderEmail-trigger")
                    .WithCronSchedule("0 0 8 * * ?", x => x
                        .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"))  // 8:00 AM EST daily
                    )
                );

                var deleteArchivedUsersKey = new JobKey("DeleteArchivedUsers");
                q.AddJob<DeleteArchivedUsersJob>(opts => opts
                    .WithIdentity(deleteArchivedUsersKey)
                    .DisallowConcurrentExecution()
                );

                q.AddTrigger(opts => opts
                    .ForJob(deleteArchivedUsersKey)
                    .WithIdentity("DeleteArchivedUsers-trigger")
                    .WithCronSchedule("0 0 2 * * ?", x => x
                        .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"))  // 2:00 AM EST daily
                    )
                );

                var deleteTestUsersJobKey = new JobKey("DeleteTestUsers");
                q.AddJob<DeleteTestUsersJob>(opts => opts
                    .WithIdentity(deleteTestUsersJobKey)
                    .DisallowConcurrentExecution()
                );

                q.AddTrigger(opts => opts
                    .ForJob(deleteTestUsersJobKey)
                    .WithIdentity("DeleteTestUsers-trigger")
                    .WithCronSchedule("0 0 18 * * ?", x => x
                        .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("India Standard Time")) // 6:00 PM IST daily
                    )
                );
            });

            services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
        }
    }
}

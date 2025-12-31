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
                var jobKey = new JobKey("SendDAFReminderEmail");
                q.AddJob<SendDAFReminderEmail>(opts => opts.WithIdentity(jobKey));

                // Run daily at 8:00 AM EST
                q.AddTrigger(opts => opts
                    .ForJob(jobKey)
                    .WithIdentity("SendDAFReminderEmail-trigger")
                    .WithCronSchedule("0 0 8 * * ?", x => x
                        .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"))
                    )
                );
            });

            services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
        }
    }
}

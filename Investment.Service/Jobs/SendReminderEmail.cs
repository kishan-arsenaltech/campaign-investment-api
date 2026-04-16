using Investment.Service.Interfaces;
using Quartz;

namespace Investment.Service.Jobs
{
    public class SendReminderEmail : IJob
    {
        private readonly IEmailJobService _emailJobService;

        public SendReminderEmail(IEmailJobService emailJobService)
        {
            _emailJobService = emailJobService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _emailJobService.SendReminderEmailsAsync(context.JobDetail.Key.Name);
        }
    }
}

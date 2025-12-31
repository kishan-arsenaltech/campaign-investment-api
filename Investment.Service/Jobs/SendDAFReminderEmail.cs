using Investment.Service.Interfaces;
using Quartz;

namespace Investment.Service.Jobs
{
    public class SendDAFReminderEmail : IJob
    {
        private readonly IEmailJobService _emailJobService;

        public SendDAFReminderEmail(IEmailJobService emailJobService)
        {
            _emailJobService = emailJobService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await _emailJobService.SendDafReminderEmailsAsync();
        }
    }
}

namespace Investment.Service.Interfaces
{
    public interface IEmailJobService
    {
        Task SendReminderEmailsAsync(string jobName);
    }
}

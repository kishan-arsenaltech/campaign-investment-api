namespace Investment.Service.Interfaces
{
    public interface IEmailJobService
    {
        Task SendDafReminderEmailsAsync();
    }
}

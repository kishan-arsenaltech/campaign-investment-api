namespace Investment.Service.Interfaces
{
    public interface IEmailQueue
    {
        void QueueEmail(Func<IServiceProvider, Task> workItem);
    }
}

namespace Investment.Service.Interfaces
{
    public interface IDeleteTestUsersJobService
    {
        Task RunDeleteAsync(CancellationToken cancellationToken = default);
    }
}

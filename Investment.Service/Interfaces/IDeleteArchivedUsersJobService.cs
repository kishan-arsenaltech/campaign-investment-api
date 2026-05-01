namespace Investment.Service.Interfaces
{
    public interface IDeleteArchivedUsersJobService
    {
        Task RunCleanupAsync(CancellationToken cancellationToken = default);
    }
}

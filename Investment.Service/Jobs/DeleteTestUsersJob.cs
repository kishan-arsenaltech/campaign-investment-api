using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Quartz;

namespace Investment.Service.Jobs
{
    public class DeleteTestUsersJob : IJob
    {
        private readonly IDeleteTestUsersJobService _service;
        private readonly RepositoryContext _context;

        public DeleteTestUsersJob(IDeleteTestUsersJobService service, RepositoryContext context)
        {
            _service = service;
            _context = context;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var logEntry = new SchedulerLogs
            {
                StartTime = DateTime.Now,
                JobName = context.JobDetail.Key.Name
            };

            try
            {
                await _service.RunDeleteAsync(context.CancellationToken);
            }
            catch (Exception ex)
            {
                logEntry.ErrorMessage = ex.ToString();
                throw new JobExecutionException(ex, refireImmediately: false);
            }
            finally
            {
                logEntry.EndTime = DateTime.Now;
                logEntry.Day3EmailCount = 0;
                logEntry.Week2EmailCount = 0;

                await _context.AddAsync(logEntry);
                await _context.SaveChangesAsync();
            }
        }
    }
}

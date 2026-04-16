using Investment.Service.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Investment.Service.Jobs
{
    public class EmailQueueWorker : BackgroundService
    {
        private readonly EmailQueue _queue;
        private readonly IServiceProvider _serviceProvider;

        public EmailQueueWorker(EmailQueue queue, IServiceProvider serviceProvider)
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await _queue.DequeueAsync(stoppingToken);

                using var scope = _serviceProvider.CreateScope();

                await workItem(scope.ServiceProvider);
            }
        }
    }
}

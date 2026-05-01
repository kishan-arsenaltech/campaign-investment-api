using Investment.Service.Interfaces;
using System.Collections.Concurrent;

namespace Investment.Service.Services
{
    public class EmailQueue : IEmailQueue
    {
        private readonly ConcurrentQueue<Func<IServiceProvider, Task>> _workItems = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void QueueEmail(Func<IServiceProvider, Task> workItem)
        {
            if (workItem == null)
                throw new ArgumentNullException(nameof(workItem));

            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        public async Task<Func<IServiceProvider, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);

            _workItems.TryDequeue(out var workItem);

            return workItem!;
        }
    }
}

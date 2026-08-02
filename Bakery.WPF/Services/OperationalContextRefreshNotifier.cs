namespace Bakery.WPF.Services;

public interface IOperationalContextRefreshNotifier
{
    event Func<Task>? RefreshRequested;
    Task RequestRefreshAsync();
}

public sealed class OperationalContextRefreshNotifier : IOperationalContextRefreshNotifier
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event Func<Task>? RefreshRequested;

    public async Task RequestRefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var handlers = RefreshRequested;
            if (handlers is null) return;

            
            foreach (Func<Task> handler in handlers.GetInvocationList())
            {
                await handler();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}

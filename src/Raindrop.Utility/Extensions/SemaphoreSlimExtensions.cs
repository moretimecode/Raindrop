namespace Raindrop.Utility.Extensions;

public static class SemaphoreSlimExtensions
{

    public static async ValueTask<IAsyncDisposable> EnterAsync(
        this SemaphoreSlim semaphore,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(semaphore);

        bool acquired = await semaphore.WaitAsync(
            TimeSpan.FromSeconds(timeoutSeconds),
            cancellationToken);

        if (!acquired)
        {
            throw new TimeoutException(
                $"未能在 {timeoutSeconds} 秒内获取信号量。");
        }

        return new SemaphoreLease(semaphore);
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            SemaphoreSlim? semaphore =
                Interlocked.Exchange(ref _semaphore, null);

            semaphore?.Release();

            return ValueTask.CompletedTask;
        }
    }
}

namespace OnlyRag.Api.Ollama;

internal sealed class OllamaGenerationCoordinator
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreLease(semaphore);
    }

    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }

    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

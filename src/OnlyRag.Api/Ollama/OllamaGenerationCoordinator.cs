namespace OnlyRag.Api.Ollama;

internal sealed class OllamaGenerationCoordinator
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

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
}

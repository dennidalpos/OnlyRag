namespace OnlyRag.Worker;

public sealed record LocalJobQueueDescriptor(string QueueName, bool Persistent, int MaxParallelJobs, int MaxRetries)
{
    public static LocalJobQueueDescriptor Default { get; } = new(
        "default",
        Persistent: true,
        MaxParallelJobs: 1,
        MaxRetries: 5);
}

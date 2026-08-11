namespace OnlyRag.Jobs.Abstractions;

public interface ILocalJobHandler
{
    string Type { get; }

    Task ExecuteAsync(LocalJob job, ILocalJobQueue queue, CancellationToken cancellationToken);
}

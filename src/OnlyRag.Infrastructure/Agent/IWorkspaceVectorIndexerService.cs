namespace OnlyRag.Infrastructure.Agent;

public interface IWorkspaceVectorIndexerService
{
    Task IndexWorkspaceFileAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken = default);
}

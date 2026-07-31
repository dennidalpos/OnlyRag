namespace OnlyRag.Infrastructure.Agent;

public record AstDependencyNode(
    string FilePath,
    IReadOnlyList<string> DefinedSymbols,
    IReadOnlyList<string> ImportedModules,
    IReadOnlyList<string> ReferencedSymbols);

public record AstDependencyGraphResult(
    string TargetFile,
    IReadOnlyList<string> DirectDependencies,
    IReadOnlyList<string> DependentFiles,
    string MermaidDiagram);

public interface IAstDependencyGraphService
{
    Task<AstDependencyGraphResult> AnalyzeWorkspaceDependenciesAsync(
        string workspaceRoot,
        string targetFilePath,
        CancellationToken cancellationToken = default);
}

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Infrastructure.Agent;

public sealed class AstDependencyGraphService : IAstDependencyGraphService
{
    private static readonly Regex TsImportRegex = new(@"import\s+.*?from\s+['""]([^'""]+)['""]", RegexOptions.Compiled);
    private static readonly Regex TsExportRegex = new(@"export\s+(?:default\s+)?(?:class|function|interface|type|enum|const|let|var)\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);

    private readonly ILoggingService? logger;

    public AstDependencyGraphService(ILoggingService? logger = null)
    {
        this.logger = logger;
    }

    public async Task<AstDependencyGraphResult> AnalyzeWorkspaceDependenciesAsync(
        string workspaceRoot,
        string targetFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return new AstDependencyGraphResult(targetFilePath, Array.Empty<string>(), Array.Empty<string>(), "graph TD\n  EmptyWorkspace[\"Workspace Root Non Definito\"]");
        }

        string cleanTargetRel = targetFilePath.Replace('\\', '/').TrimStart('/');
        string targetFullPath = Path.GetFullPath(Path.Combine(workspaceRoot, cleanTargetRel));

        var files = Directory.EnumerateFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\node_modules\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\.git\\"))
            .Where(IsSupportedCodeFile)
            .ToList();

        var nodes = new Dictionary<string, AstDependencyNode>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(workspaceRoot, file).Replace('\\', '/');
            string text = await File.ReadAllTextAsync(file, cancellationToken);
            nodes[rel] = ParseNode(rel, text);
        }

        var directDependencies = new List<string>();
        var dependentFiles = new List<string>();

        if (nodes.TryGetValue(cleanTargetRel, out var targetNode))
        {
            foreach (var kvp in nodes)
            {
                if (kvp.Key.Equals(cleanTargetRel, StringComparison.OrdinalIgnoreCase)) continue;

                // Checks if target imports/references kvp.Key
                if (targetNode.ImportedModules.Any(m => kvp.Key.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
                    targetNode.ReferencedSymbols.Any(s => kvp.Value.DefinedSymbols.Any(def => def.Equals(s, StringComparison.OrdinalIgnoreCase))))
                {
                    if (!directDependencies.Contains(kvp.Key)) directDependencies.Add(kvp.Key);
                }

                // Checks if kvp.Key references targetNode defined symbols
                if (kvp.Value.ImportedModules.Any(m => cleanTargetRel.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
                    kvp.Value.ReferencedSymbols.Any(s => targetNode.DefinedSymbols.Any(def => def.Equals(s, StringComparison.OrdinalIgnoreCase))))
                {
                    if (!dependentFiles.Contains(kvp.Key)) dependentFiles.Add(kvp.Key);
                }
            }
        }

        string mermaid = BuildMermaidDiagram(cleanTargetRel, directDependencies, dependentFiles);
        logger?.LogInfo("AstGraph", $"Roslyn AST analysis completed for '{cleanTargetRel}': {directDependencies.Count} direct dependencies, {dependentFiles.Count} dependent files.");

        return new AstDependencyGraphResult(cleanTargetRel, directDependencies, dependentFiles, mermaid);
    }

    private static bool IsSupportedCodeFile(string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        return ext is ".cs" or ".ts" or ".tsx" or ".js" or ".jsx";
    }

    private static AstDependencyNode ParseNode(string relativePath, string content)
    {
        if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var tree = CSharpSyntaxTree.ParseText(content);
            var root = tree.GetRoot();
            var visitor = new CsRoslynSyntaxVisitor();
            visitor.Visit(root);

            return new AstDependencyNode(relativePath, visitor.DefinedSymbols.Distinct().ToList(), visitor.ImportedModules.Distinct().ToList(), visitor.ReferencedSymbols.Distinct().ToList());
        }

        var definedSymbols = new List<string>();
        var importedModules = new List<string>();
        var referencedSymbols = new List<string>();

        foreach (Match m in TsImportRegex.Matches(content))
        {
            importedModules.Add(m.Groups[1].Value);
        }
        foreach (Match m in TsExportRegex.Matches(content))
        {
            definedSymbols.Add(m.Groups[1].Value);
        }

        return new AstDependencyNode(relativePath, definedSymbols.Distinct().ToList(), importedModules.Distinct().ToList(), referencedSymbols.Distinct().ToList());
    }

    private static string BuildMermaidDiagram(string targetFile, List<string> deps, List<string> dependents)
    {
        var sb = new StringBuilder();
        sb.AppendLine("```mermaid");
        sb.AppendLine("graph TD");

        string targetId = SanitizeNodeId(targetFile);
        sb.AppendLine($"  {targetId}[\"{Path.GetFileName(targetFile)} (Target)\"]");

        foreach (string dep in deps.Take(8))
        {
            string depId = SanitizeNodeId(dep);
            sb.AppendLine($"  {targetId} --> {depId}[\"{Path.GetFileName(dep)}\"]");
        }

        foreach (string dep in dependents.Take(8))
        {
            string depId = SanitizeNodeId(dep);
            sb.AppendLine($"  {depId}[\"{Path.GetFileName(dep)}\"] --> {targetId}");
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string SanitizeNodeId(string path)
    {
        return "N_" + Math.Abs(path.GetHashCode());
    }
}

internal sealed class CsRoslynSyntaxVisitor : CSharpSyntaxWalker
{
    public List<string> DefinedSymbols { get; } = new();
    public List<string> ImportedModules { get; } = new();
    public List<string> ReferencedSymbols { get; } = new();

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name != null)
        {
            ImportedModules.Add(node.Name.ToString());
        }
        base.VisitUsingDirective(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        DefinedSymbols.Add(node.Identifier.ValueText);
        base.VisitClassDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        DefinedSymbols.Add(node.Identifier.ValueText);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        DefinedSymbols.Add(node.Identifier.ValueText);
        base.VisitStructDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        DefinedSymbols.Add(node.Identifier.ValueText);
        base.VisitRecordDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        DefinedSymbols.Add(node.Identifier.ValueText);
        base.VisitEnumDeclaration(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        string name = node.Identifier.ValueText;
        if (!string.IsNullOrWhiteSpace(name) && char.IsUpper(name[0]))
        {
            ReferencedSymbols.Add(name);
        }
        base.VisitIdentifierName(node);
    }
}

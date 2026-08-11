using System.Text.RegularExpressions;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval.Graph;

public record AstSymbolNode(
    string SymbolId,
    string SymbolName,
    string SymbolKind,
    string FilePath,
    int LineNumber,
    IReadOnlyList<string> Callers,
    IReadOnlyList<string> Callees,
    int ProximityDepth,
    string CodeSnippet
);

public record AstGraphAnomaly(
    string Type,
    string Severity,
    string SymbolId,
    string SymbolName,
    string Description
);

public record AstGraphValidationResult(
    bool IsValid,
    double SemanticAlignmentScore,
    int TotalSymbols,
    int TotalEdges,
    IReadOnlyList<AstGraphAnomaly> Anomalies
);

public interface IGraphRagAstSymbolIndexer
{
    IReadOnlyList<AstSymbolNode> ExtractAstSymbols(string filePath, string fileContent);
    IReadOnlyList<AstSymbolNode> ExtractWorkspaceAstSymbols(string workspaceDirectory);
    string CreateSymbolVectorRepresentation(AstSymbolNode symbol);
    (IReadOnlyList<EntityGraphNode> Nodes, IReadOnlyList<EntityGraphEdge> Edges) ConvertToGraphRepresentation(IReadOnlyList<AstSymbolNode> symbols);
    AstGraphValidationResult ValidateAstGraph(IReadOnlyList<AstSymbolNode> symbols);
    Task IndexWorkspaceSymbolsAsync(IGraphRetrievalService graphService, string workspaceDirectory, CancellationToken cancellationToken = default);
}

public sealed class GraphRagAstSymbolIndexer : IGraphRagAstSymbolIndexer
{
    private static readonly Regex ClassRegex = new(
        @"(?:public|internal|private|protected)?\s*(?:sealed|abstract|partial)?\s*(?:class|interface|struct|record)\s+([A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex MethodRegex = new(
        @"(?:public|internal|private|protected)?\s*(?:async|virtual|override|static|sealed)?\s*[A-Za-z0-9_<>?, ]+\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex TsJsClassOrInterfaceRegex = new(
        @"(?:export\s+)?(?:default\s+)?(?:class|interface|type)\s+([A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex TsJsFunctionRegex = new(
        @"(?:export\s+)?(?:async\s+)?function\s+([A-Za-z0-9_]+)\s*\(|(?:const|let|var)\s+([A-Za-z0-9_]+)\s*=\s*(?:async\s*)?\(",
        RegexOptions.Compiled);

    private static readonly Regex PyClassRegex = new(
        @"class\s+([A-Za-z0-9_]+)\s*(?:\([^)]*\))?:",
        RegexOptions.Compiled);

    private static readonly Regex PyDefRegex = new(
        @"def\s+([A-Za-z0-9_]+)\s*\(",
        RegexOptions.Compiled);

    public IReadOnlyList<AstSymbolNode> ExtractWorkspaceAstSymbols(string workspaceDirectory)
    {
        if (string.IsNullOrWhiteSpace(workspaceDirectory) || !Directory.Exists(workspaceDirectory))
        {
            return Array.Empty<AstSymbolNode>();
        }

        var allSymbols = new List<AstSymbolNode>();
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py" };

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(workspaceDirectory, "*.*", SearchOption.AllDirectories))
            {
                if (filePath.Contains("\\bin\\") || filePath.Contains("\\obj\\") || filePath.Contains("\\node_modules\\") || filePath.Contains("\\.git\\"))
                {
                    continue;
                }

                string ext = Path.GetExtension(filePath);
                if (allowedExtensions.Contains(ext))
                {
                    try
                    {
                        string content = File.ReadAllText(filePath);
                        var symbols = ExtractAstSymbols(filePath, content);
                        allSymbols.AddRange(symbols);
                    }
                    catch { }
                }
            }
        }
        catch { }

        return allSymbols;
    }

    public IReadOnlyList<AstSymbolNode> ExtractAstSymbols(string filePath, string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent)) return Array.Empty<AstSymbolNode>();

        var symbols = new List<AstSymbolNode>();
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string[] lines = fileContent.Split('\n');

        var detectedSymbolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: extract symbol definitions
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int lineNumber = i + 1;
            int indentDepth = (line.Length - line.TrimStart().Length) / 4;

            if (ext is ".cs")
            {
                var classMatch = ClassRegex.Match(line);
                if (classMatch.Success && classMatch.Groups.Count >= 2)
                {
                    string className = classMatch.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(className))
                    {
                        detectedSymbolNames.Add(className);
                        symbols.Add(new AstSymbolNode(
                            SymbolId: $"ast_{Guid.NewGuid():N}"[..12],
                            SymbolName: className,
                            SymbolKind: "Class/Interface",
                            FilePath: filePath,
                            LineNumber: lineNumber,
                            Callers: Array.Empty<string>(),
                            Callees: Array.Empty<string>(),
                            ProximityDepth: indentDepth,
                            CodeSnippet: line.Trim()));
                    }
                }

                var methodMatch = MethodRegex.Match(line);
                if (methodMatch.Success && methodMatch.Groups.Count >= 2)
                {
                    string methodName = methodMatch.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(methodName) && !IsKeyword(methodName))
                    {
                        detectedSymbolNames.Add(methodName);
                        symbols.Add(new AstSymbolNode(
                            SymbolId: $"ast_{Guid.NewGuid():N}"[..12],
                            SymbolName: methodName,
                            SymbolKind: "Method",
                            FilePath: filePath,
                            LineNumber: lineNumber,
                            Callers: Array.Empty<string>(),
                            Callees: Array.Empty<string>(),
                            ProximityDepth: Math.Max(1, indentDepth),
                            CodeSnippet: line.Trim()));
                    }
                }
            }
            else if (ext is ".ts" or ".tsx" or ".js" or ".jsx")
            {
                var classOrTypeMatch = TsJsClassOrInterfaceRegex.Match(line);
                if (classOrTypeMatch.Success && classOrTypeMatch.Groups.Count >= 2)
                {
                    string typeName = classOrTypeMatch.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(typeName))
                    {
                        detectedSymbolNames.Add(typeName);
                        symbols.Add(new AstSymbolNode(
                            SymbolId: $"ast_{Guid.NewGuid():N}"[..12],
                            SymbolName: typeName,
                            SymbolKind: "Class/Interface/Type",
                            FilePath: filePath,
                            LineNumber: lineNumber,
                            Callers: Array.Empty<string>(),
                            Callees: Array.Empty<string>(),
                            ProximityDepth: indentDepth,
                            CodeSnippet: line.Trim()));
                    }
                }

                var fnMatch = TsJsFunctionRegex.Match(line);
                if (fnMatch.Success)
                {
                    string fnName = !string.IsNullOrWhiteSpace(fnMatch.Groups[1].Value) ? fnMatch.Groups[1].Value : fnMatch.Groups[2].Value;
                    if (!string.IsNullOrWhiteSpace(fnName))
                    {
                        detectedSymbolNames.Add(fnName);
                        symbols.Add(new AstSymbolNode(
                            SymbolId: $"ast_{Guid.NewGuid():N}"[..12],
                            SymbolName: fnName,
                            SymbolKind: "Function",
                            FilePath: filePath,
                            LineNumber: lineNumber,
                            Callers: Array.Empty<string>(),
                            Callees: Array.Empty<string>(),
                            ProximityDepth: Math.Max(1, indentDepth),
                            CodeSnippet: line.Trim()));
                    }
                }
            }
            else if (ext is ".py")
            {
                var pyClassMatch = PyClassRegex.Match(line);
                if (pyClassMatch.Success && pyClassMatch.Groups.Count >= 2)
                {
                    string pyClass = pyClassMatch.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(pyClass))
                    {
                        detectedSymbolNames.Add(pyClass);
                        symbols.Add(new AstSymbolNode(
                            SymbolId: $"ast_{Guid.NewGuid():N}"[..12],
                            SymbolName: pyClass,
                            SymbolKind: "Class",
                            FilePath: filePath,
                            LineNumber: lineNumber,
                            Callers: Array.Empty<string>(),
                            Callees: Array.Empty<string>(),
                            ProximityDepth: indentDepth,
                            CodeSnippet: line.Trim()));
                    }
                }

                var pyMatch = PyDefRegex.Match(line);
                if (pyMatch.Success && pyMatch.Groups.Count >= 2)
                {
                    string pyFn = pyMatch.Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(pyFn))
                    {
                        detectedSymbolNames.Add(pyFn);
                        symbols.Add(new AstSymbolNode(
                            SymbolId: $"ast_{Guid.NewGuid():N}"[..12],
                            SymbolName: pyFn,
                            SymbolKind: "Function",
                            FilePath: filePath,
                            LineNumber: lineNumber,
                            Callers: Array.Empty<string>(),
                            Callees: Array.Empty<string>(),
                            ProximityDepth: Math.Max(1, indentDepth),
                            CodeSnippet: line.Trim()));
                    }
                }
            }
        }

        // Second pass: resolving callee dependencies based on symbol references
        var enrichedSymbols = new List<AstSymbolNode>();
        foreach (var s in symbols)
        {
            var callees = new List<string>();
            foreach (var targetName in detectedSymbolNames)
            {
                if (!targetName.Equals(s.SymbolName, StringComparison.OrdinalIgnoreCase) && fileContent.Contains(targetName))
                {
                    callees.Add(targetName);
                }
            }

            enrichedSymbols.Add(s with { Callees = callees.Take(10).ToArray() });
        }

        return enrichedSymbols;
    }

    public string CreateSymbolVectorRepresentation(AstSymbolNode symbol)
    {
        string callersStr = symbol.Callers.Count > 0 ? string.Join(", ", symbol.Callers) : "None";
        string calleesStr = symbol.Callees.Count > 0 ? string.Join(", ", symbol.Callees) : "None";

        return $"[AST SYMBOL NODE] Name: {symbol.SymbolName} | Kind: {symbol.SymbolKind} | File: {symbol.FilePath}:{symbol.LineNumber}\n" +
               $"Proximity Depth: {symbol.ProximityDepth} | Callers: {callersStr} | Callees: {calleesStr}\n" +
               $"Snippet: {symbol.CodeSnippet}";
    }

    public (IReadOnlyList<EntityGraphNode> Nodes, IReadOnlyList<EntityGraphEdge> Edges) ConvertToGraphRepresentation(IReadOnlyList<AstSymbolNode> symbols)
    {
        List<EntityGraphNode> nodes = new(symbols.Count);
        List<EntityGraphEdge> edges = new();
        Dictionary<string, string> symbolNameIdMap = new(StringComparer.OrdinalIgnoreCase);

        foreach (var s in symbols)
        {
            symbolNameIdMap[s.SymbolName] = s.SymbolId;
            string description = $"{s.SymbolKind} defined in {Path.GetFileName(s.FilePath)}:{s.LineNumber}. Snippet: {s.CodeSnippet}";
            nodes.Add(new EntityGraphNode(
                s.SymbolId,
                DocumentId: "",
                ChunkId: "",
                Name: s.SymbolName,
                Type: s.SymbolKind,
                Description: description));
        }

        foreach (var s in symbols)
        {
            foreach (var calleeName in s.Callees)
            {
                if (symbolNameIdMap.TryGetValue(calleeName, out string? calleeId))
                {
                    edges.Add(new EntityGraphEdge(
                        EdgeId: $"edge_{Guid.NewGuid():N}"[..12],
                        SourceNodeId: s.SymbolId,
                        TargetNodeId: calleeId,
                        RelationType: "CALLS",
                        Weight: 1.0f,
                        ChunkId: ""));
                }
            }
        }

        return (nodes, edges);
    }

    public AstGraphValidationResult ValidateAstGraph(IReadOnlyList<AstSymbolNode> symbols)
    {
        if (symbols == null || symbols.Count == 0)
        {
            return new AstGraphValidationResult(true, 1.0, 0, 0, Array.Empty<AstGraphAnomaly>());
        }

        var anomalies = new List<AstGraphAnomaly>();
        var symbolMap = symbols.ToDictionary(s => s.SymbolName, StringComparer.OrdinalIgnoreCase);
        var (nodes, edges) = ConvertToGraphRepresentation(symbols);

        foreach (var symbol in symbols)
        {
            if (symbol.Callers.Count == 0 && symbol.Callees.Count == 0)
            {
                anomalies.Add(new AstGraphAnomaly(
                    Type: "OrphanSymbol",
                    Severity: "Warning",
                    SymbolId: symbol.SymbolId,
                    SymbolName: symbol.SymbolName,
                    Description: $"Symbol '{symbol.SymbolName}' has no callers or callees in the AST graph."));
            }

            foreach (var callee in symbol.Callees)
            {
                if (!symbolMap.ContainsKey(callee))
                {
                    anomalies.Add(new AstGraphAnomaly(
                        Type: "UnresolvedCallee",
                        Severity: "Info",
                        SymbolId: symbol.SymbolId,
                        SymbolName: symbol.SymbolName,
                        Description: $"Symbol '{symbol.SymbolName}' references external callee '{callee}' which is not indexed in current workspace."));
                }
            }

            foreach (var callee in symbol.Callees)
            {
                if (symbolMap.TryGetValue(callee, out var calleeSymbol) && calleeSymbol.Callees.Contains(symbol.SymbolName, StringComparer.OrdinalIgnoreCase))
                {
                    anomalies.Add(new AstGraphAnomaly(
                        Type: "CircularDependency",
                        Severity: "Warning",
                        SymbolId: symbol.SymbolId,
                        SymbolName: symbol.SymbolName,
                        Description: $"Direct circular call detected between '{symbol.SymbolName}' and '{callee}'."));
                }
            }
        }

        double anomalyFactor = Math.Min(1.0, (double)anomalies.Count / Math.Max(1, symbols.Count));
        double semanticAlignmentScore = Math.Round(Math.Max(0.0, 1.0 - (anomalyFactor * 0.5)), 4);
        bool isValid = !anomalies.Any(a => a.Severity == "Error");

        return new AstGraphValidationResult(
            IsValid: isValid,
            SemanticAlignmentScore: semanticAlignmentScore,
            TotalSymbols: symbols.Count,
            TotalEdges: edges.Count,
            Anomalies: anomalies);
    }

    public async Task IndexWorkspaceSymbolsAsync(
        IGraphRetrievalService graphService,
        string workspaceDirectory,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AstSymbolNode> symbols = ExtractWorkspaceAstSymbols(workspaceDirectory);
        if (symbols.Count == 0) return;

        var (nodes, edges) = ConvertToGraphRepresentation(symbols);
        await graphService.InsertGraphAsync(nodes, edges, cancellationToken);
    }

    private static bool IsKeyword(string name)
    {
        return name is "if" or "while" or "for" or "foreach" or "switch" or "catch" or "using" or "return" or "new";
    }
}

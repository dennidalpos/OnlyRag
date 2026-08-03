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

public interface IGraphRagAstSymbolIndexer
{
    IReadOnlyList<AstSymbolNode> ExtractAstSymbols(string filePath, string fileContent);
    string CreateSymbolVectorRepresentation(AstSymbolNode symbol);
}

public sealed class GraphRagAstSymbolIndexer : IGraphRagAstSymbolIndexer
{
    private static readonly Regex ClassRegex = new(
        @"(?:public|internal|private|protected)?\s*(?:sealed|abstract|partial)?\s*(?:class|interface|struct|record)\s+([A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex MethodRegex = new(
        @"(?:public|internal|private|protected)?\s*(?:async|virtual|override|static|sealed)?\s*[A-Za-z0-9_<>?, ]+\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex TsJsFunctionRegex = new(
        @"(?:export\s+)?(?:async\s+)?function\s+([A-Za-z0-9_]+)\s*\(|(?:const|let|var)\s+([A-Za-z0-9_]+)\s*=\s*(?:async\s*)?\(",
        RegexOptions.Compiled);

    private static readonly Regex PyDefRegex = new(
        @"def\s+([A-Za-z0-9_]+)\s*\(",
        RegexOptions.Compiled);

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
                            ProximityDepth: 0,
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
                            ProximityDepth: 1,
                            CodeSnippet: line.Trim()));
                    }
                }
            }
            else if (ext is ".ts" or ".tsx" or ".js" or ".jsx")
            {
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
                            ProximityDepth: 1,
                            CodeSnippet: line.Trim()));
                    }
                }
            }
            else if (ext is ".py")
            {
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
                            ProximityDepth: 1,
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

    private static bool IsKeyword(string name)
    {
        return name is "if" or "while" or "for" or "foreach" or "switch" or "catch" or "using" or "return" or "new";
    }
}

using OnlyRag.Infrastructure.Retrieval.Graph;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public class GraphRagAstSymbolIndexerTests
{
    [Fact]
    public void ExtractAstSymbols_ParsesCSharpClassesAndMethods()
    {
        string csharpCode = """
            namespace Example;

            public sealed class SampleService
            {
                public async Task DoWorkAsync()
                {
                    await ProcessDataAsync();
                }

                private async Task ProcessDataAsync()
                {
                }
            }
            """;

        var indexer = new GraphRagAstSymbolIndexer();
        var symbols = indexer.ExtractAstSymbols("src/SampleService.cs", csharpCode);

        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.SymbolName == "SampleService" && s.SymbolKind == "Class/Interface");
        Assert.Contains(symbols, s => s.SymbolName == "DoWorkAsync" && s.SymbolKind == "Method");
        Assert.Contains(symbols, s => s.SymbolName == "ProcessDataAsync" && s.SymbolKind == "Method");

        var doWorkSymbol = symbols.First(s => s.SymbolName == "DoWorkAsync");
        Assert.Contains("ProcessDataAsync", doWorkSymbol.Callees);
    }

    [Fact]
    public void CreateSymbolVectorRepresentation_GeneratesFormattedMetadataString()
    {
        var node = new AstSymbolNode(
            SymbolId: "ast123",
            SymbolName: "ExecuteTask",
            SymbolKind: "Method",
            FilePath: "src/Agent.cs",
            LineNumber: 42,
            Callers: new[] { "Main" },
            Callees: new[] { "ValidateInput" },
            ProximityDepth: 1,
            CodeSnippet: "public void ExecuteTask()");

        var indexer = new GraphRagAstSymbolIndexer();
        string representation = indexer.CreateSymbolVectorRepresentation(node);

        Assert.Contains("[AST SYMBOL NODE]", representation);
        Assert.Contains("Name: ExecuteTask", representation);
        Assert.Contains("File: src/Agent.cs:42", representation);
        Assert.Contains("Callers: Main", representation);
        Assert.Contains("Callees: ValidateInput", representation);
    }

    [Fact]
    public void ExtractWorkspaceAstSymbols_ParsesFilesRecursively()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ast_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string subDir = Path.Combine(tempDir, "sub");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(tempDir, "App.cs"), "public class AppHost { public void Run() {} }");
            File.WriteAllText(Path.Combine(subDir, "helper.py"), "class Helper:\n    def do_stuff(self):\n        pass");

            var indexer = new GraphRagAstSymbolIndexer();
            var symbols = indexer.ExtractWorkspaceAstSymbols(tempDir);

            Assert.True(symbols.Count >= 3);
            Assert.Contains(symbols, s => s.SymbolName == "AppHost");
            Assert.Contains(symbols, s => s.SymbolName == "Helper");
            Assert.Contains(symbols, s => s.SymbolName == "do_stuff");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ConvertToGraphRepresentation_MapsAstSymbolsToGraphNodesAndEdges()
    {
        var symbols = new[]
        {
            new AstSymbolNode("ast1", "CallerMethod", "Method", "App.cs", 10, Array.Empty<string>(), new[] { "CalleeMethod" }, 1, "public void CallerMethod()"),
            new AstSymbolNode("ast2", "CalleeMethod", "Method", "App.cs", 20, Array.Empty<string>(), Array.Empty<string>(), 1, "public void CalleeMethod()")
        };

        var indexer = new GraphRagAstSymbolIndexer();
        var (nodes, edges) = indexer.ConvertToGraphRepresentation(symbols);

        Assert.Equal(2, nodes.Count);
        Assert.Single(edges);
        Assert.Equal("ast1", edges[0].SourceNodeId);
        Assert.Equal("ast2", edges[0].TargetNodeId);
        Assert.Equal("CALLS", edges[0].RelationType);
    }

    [Fact]
    public void ValidateAstGraph_DetectsOrphansAndUnresolvedCallees()
    {
        var symbols = new[]
        {
            new AstSymbolNode("ast1", "CallerMethod", "Method", "App.cs", 10, Array.Empty<string>(), new[] { "ExternalMethod" }, 1, "public void CallerMethod()"),
            new AstSymbolNode("ast2", "OrphanMethod", "Method", "App.cs", 30, Array.Empty<string>(), Array.Empty<string>(), 1, "public void OrphanMethod()")
        };

        var indexer = new GraphRagAstSymbolIndexer();
        var validation = indexer.ValidateAstGraph(symbols);

        Assert.True(validation.IsValid);
        Assert.Equal(2, validation.TotalSymbols);
        Assert.Contains(validation.Anomalies, a => a.Type == "OrphanSymbol" && a.SymbolName == "OrphanMethod");
        Assert.Contains(validation.Anomalies, a => a.Type == "UnresolvedCallee" && a.SymbolName == "CallerMethod");
        Assert.True(validation.SemanticAlignmentScore > 0.0 && validation.SemanticAlignmentScore <= 1.0);
    }
}

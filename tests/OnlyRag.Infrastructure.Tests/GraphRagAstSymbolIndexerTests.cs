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
}

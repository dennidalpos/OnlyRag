using System.Text.Json;
using OnlyRag.Api.Mcp;
using Xunit;

namespace OnlyRag.Api.Tests;

public class McpSchemaValidatorTests
{
    [Fact]
    public void Validate_ValidArguments_ReturnsTrue()
    {
        string schemaJson = """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" }
          },
          "required": ["query"]
        }
        """;

        string argsJson = """
        {
          "query": "test query"
        }
        """;

        using var schemaDoc = JsonDocument.Parse(schemaJson);
        using var argsDoc = JsonDocument.Parse(argsJson);

        var (isValid, error) = McpSchemaValidator.Validate(schemaDoc.RootElement, argsDoc.RootElement);

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_MissingRequiredArgument_ReturnsFalse()
    {
        string schemaJson = """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" }
          },
          "required": ["query"]
        }
        """;

        string argsJson = "{}";

        using var schemaDoc = JsonDocument.Parse(schemaJson);
        using var argsDoc = JsonDocument.Parse(argsJson);

        var (isValid, error) = McpSchemaValidator.Validate(schemaDoc.RootElement, argsDoc.RootElement);

        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("query", error);
    }
}

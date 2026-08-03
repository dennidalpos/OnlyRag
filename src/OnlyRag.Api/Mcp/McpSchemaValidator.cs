using System.Text.Json;

namespace OnlyRag.Api.Mcp;

public static class McpSchemaValidator
{
    public static (bool IsValid, string? ErrorMessage) Validate(JsonElement schema, JsonElement arguments)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return (true, null);
        }

        if (schema.TryGetProperty("type", out JsonElement typeProp) &&
            typeProp.ValueKind == JsonValueKind.String &&
            typeProp.GetString() == "object")
        {
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                return (false, "I parametri forniti devono essere un oggetto JSON.");
            }

            if (schema.TryGetProperty("required", out JsonElement requiredProp) &&
                requiredProp.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement req in requiredProp.EnumerateArray())
                {
                    if (req.ValueKind == JsonValueKind.String)
                    {
                        string propName = req.GetString()!;
                        if (!arguments.TryGetProperty(propName, out JsonElement val) ||
                            val.ValueKind == JsonValueKind.Null ||
                            val.ValueKind == JsonValueKind.Undefined)
                        {
                            return (false, $"Parametro obbligatorio mancante: '{propName}'.");
                        }
                    }
                }
            }
        }

        return (true, null);
    }
}

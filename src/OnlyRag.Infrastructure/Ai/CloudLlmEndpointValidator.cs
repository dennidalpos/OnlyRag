using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Ai;

internal static class CloudLlmEndpointValidator
{
    public static string Validate(CloudLlmProvider provider, string? endpoint)
    {
        string value = string.IsNullOrWhiteSpace(endpoint)
            ? GetDefault(provider)
            : endpoint.TrimEnd('/');

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("L'endpoint Cloud LLM deve essere un URL HTTPS valido.");
        }

        string host = uri.DnsSafeHost;
        bool allowed = provider switch
        {
            CloudLlmProvider.OpenAi => host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase),
            CloudLlmProvider.Anthropic => host.Equals("api.anthropic.com", StringComparison.OrdinalIgnoreCase),
            CloudLlmProvider.GoogleGemini => host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase),
            CloudLlmProvider.AzureOpenAi => host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"Endpoint non autorizzato per il provider {provider}.");
        }

        return value;
    }

    private static string GetDefault(CloudLlmProvider provider) => provider switch
    {
        CloudLlmProvider.OpenAi => "https://api.openai.com/v1",
        CloudLlmProvider.Anthropic => "https://api.anthropic.com/v1",
        CloudLlmProvider.GoogleGemini => "https://generativelanguage.googleapis.com/v1beta",
        CloudLlmProvider.AzureOpenAi => throw new InvalidOperationException("Endpoint Azure OpenAI obbligatorio."),
        _ => throw new InvalidOperationException("Provider Cloud LLM non supportato.")
    };
}

namespace OnlyRag.Core;

public sealed record OllamaEndpointOptions
{
    public const string DefaultBaseUrl = "http://localhost:11434";

    public Uri BaseUri { get; init; } = new(DefaultBaseUrl);
}

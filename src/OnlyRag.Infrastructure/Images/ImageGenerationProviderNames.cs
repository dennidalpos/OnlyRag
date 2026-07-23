namespace OnlyRag.Infrastructure.Images;

internal static class ImageGenerationProviderNames
{
    public const string Integrated = "integrated";

    public static string Normalize(string? provider)
    {
        string value = string.IsNullOrWhiteSpace(provider)
            ? Integrated
            : provider.Trim().ToLowerInvariant();

        return value switch
        {
            "integrated" or "local" or "onlyrag" => Integrated,
            _ => throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Provider immagini non supportato. Usa il provider integrato di OnlyRag.")
        };
    }
}

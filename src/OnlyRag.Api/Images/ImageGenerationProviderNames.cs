namespace OnlyRag.Api.Images;

internal static class ImageGenerationProviderNames
{
    public const string Automatic1111 = "automatic1111";
    public const string ComfyUi = "comfyui";

    public static string Normalize(string? provider)
    {
        string value = string.IsNullOrWhiteSpace(provider)
            ? Automatic1111
            : provider.Trim().ToLowerInvariant();

        return value switch
        {
            "automatic1111" or "a1111" or "auto1111" => Automatic1111,
            "comfyui" or "comfy" => ComfyUi,
            _ => throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Provider immagini non supportato. Usa Automatic1111 o ComfyUI.")
        };
    }
}


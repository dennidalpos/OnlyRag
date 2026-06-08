using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal static class ImageGenerationRequestValidator
{
    public static ImageGenerationRequest Normalize(ImageGenerationRequest request)
    {
        string provider = ImageGenerationProviderNames.Normalize(request.Provider);
        string prompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Inserisci un prompt per generare immagini.")
            : request.Prompt.Trim();

        string? negativePrompt = string.IsNullOrWhiteSpace(request.NegativePrompt)
            ? null
            : request.NegativePrompt.Trim();
        string? model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        int width = ClampToMultipleOfEight(request.Width, 256, 2048);
        int height = ClampToMultipleOfEight(request.Height, 256, 2048);
        int steps = Math.Clamp(request.Steps, 1, 150);
        int batchSize = Math.Clamp(request.BatchSize, 1, 4);
        long? seed = request.Seed is < 0 ? null : request.Seed;

        return new ImageGenerationRequest(
            provider,
            prompt,
            negativePrompt,
            model,
            width,
            height,
            steps,
            batchSize,
            seed);
    }

    private static int ClampToMultipleOfEight(int value, int min, int max)
    {
        int clamped = Math.Clamp(value, min, max);
        return Math.Max(min, clamped - (clamped % 8));
    }
}


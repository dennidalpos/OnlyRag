using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal interface IImageGenerationClient
{
    string Provider { get; }

    Task<ImageGenerationProviderStatus> GetStatusAsync(
        ImageGenerationSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageGenerationBinary>> GenerateAsync(
        ImageGenerationSettings settings,
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default);
}


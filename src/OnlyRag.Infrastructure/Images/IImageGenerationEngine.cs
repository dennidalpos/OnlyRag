using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Images;

public interface IImageGenerationEngine
{
    ImageGenerationEngineStatus GetStatus();

    Task<ImageGenerationEngineResult> GenerateAsync(
        ImageGenerationRequest request,
        string modelDirectory,
        bool preferGpu,
        CancellationToken cancellationToken = default);
}

public sealed record ImageGenerationEngineStatus(
    string ActiveExecutionProvider,
    string? FallbackReason,
    bool IsInitialized = false);

public sealed record ImageGenerationEngineResult(
    IReadOnlyList<ImageGenerationBinary> Images,
    string ActiveExecutionProvider,
    string? FallbackReason);

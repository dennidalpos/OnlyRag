using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal interface IImageGenerationSettingsService
{
    Task<ImageGenerationSettings> GetAsync(CancellationToken cancellationToken = default);

    Task<ImageGenerationSettings> UpdateAsync(
        ImageGenerationSettings settings,
        CancellationToken cancellationToken = default);
}


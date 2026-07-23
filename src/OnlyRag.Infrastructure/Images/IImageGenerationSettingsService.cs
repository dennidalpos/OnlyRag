using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Images;

public interface IImageGenerationSettingsService
{
    Task<ImageGenerationSettings> GetAsync(CancellationToken cancellationToken = default);

    Task<ImageGenerationSettings> UpdateAsync(
        ImageGenerationSettings settings,
        CancellationToken cancellationToken = default);
}

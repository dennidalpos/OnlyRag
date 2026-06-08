using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public interface IGeneratedImageRepository
{
    Task<GeneratedImage> CreateAsync(
        GeneratedImage image,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeneratedImage>> ListAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<(GeneratedImage Image, string RelativePath)?> GetWithPathAsync(
        long id,
        CancellationToken cancellationToken = default);
}


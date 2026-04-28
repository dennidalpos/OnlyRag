namespace OnlyRag.Infrastructure.Ocr;

public interface IOcrCacheRepository
{
    Task<OcrCacheEntry?> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    Task UpsertAsync(OcrCacheEntry entry, CancellationToken cancellationToken = default);
}

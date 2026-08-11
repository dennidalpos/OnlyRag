using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public interface ILocalStorageService
{
    Task<StorageStatusResponse> InitializeAsync(CancellationToken cancellationToken = default);

    Task<StorageStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
}

namespace OnlyRag.Infrastructure.Storage;

public interface ISettingsRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default);
}

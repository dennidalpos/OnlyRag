using OnlyRag.Core;

namespace OnlyRag.Api.Ollama;

internal interface IOllamaSettingsService
{
    Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default);

    Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default);

    Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default);
}

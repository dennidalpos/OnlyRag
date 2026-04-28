using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Ollama;

internal sealed class OllamaSettingsService : IOllamaSettingsService
{
    private const int DefaultRequestTimeoutSeconds = 120;
    private const int DefaultEmbeddingBatchSize = 1;
    private const int MinRequestTimeoutSeconds = 5;
    private const int MaxRequestTimeoutSeconds = 600;
    private const int MinEmbeddingBatchSize = 1;
    private const int MaxEmbeddingBatchSize = 8;
    private const string BaseUrlKey = "ollama.baseUrl";
    private const string DefaultChatModelKey = "ollama.defaultChatModel";
    private const string DefaultEmbeddingModelKey = "ollama.defaultEmbeddingModel";
    private const string DefaultTranslationModelKey = "ollama.defaultTranslationModel";
    private const string RequestTimeoutSecondsKey = "ollama.requestTimeoutSeconds";
    private const string EmbeddingBatchSizeKey = "ollama.embeddingBatchSize";
    private const string EmbeddingNumCtxKey = "ollama.embeddingNumCtx";
    private const int MinEmbeddingNumCtx = 64;
    private const int MaxEmbeddingNumCtx = 131072;

    private readonly ISettingsRepository settingsRepository;

    public OllamaSettingsService(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string baseUrl = await settingsRepository.GetValueAsync(BaseUrlKey, cancellationToken)
            ?? OllamaEndpointOptions.DefaultBaseUrl;
        string? defaultChatModel = NormalizeOptionalValue(
            await settingsRepository.GetValueAsync(DefaultChatModelKey, cancellationToken));
        string? defaultEmbeddingModel = NormalizeOptionalValue(
            await settingsRepository.GetValueAsync(DefaultEmbeddingModelKey, cancellationToken));
        string? defaultTranslationModel = NormalizeOptionalValue(
            await settingsRepository.GetValueAsync(DefaultTranslationModelKey, cancellationToken));
        string? requestTimeoutValue = await settingsRepository.GetValueAsync(RequestTimeoutSecondsKey, cancellationToken);
        string? embeddingBatchSizeValue = await settingsRepository.GetValueAsync(EmbeddingBatchSizeKey, cancellationToken);
        string? embeddingNumCtxValue = await settingsRepository.GetValueAsync(EmbeddingNumCtxKey, cancellationToken);

        return new OllamaSettings(
            baseUrl,
            defaultChatModel,
            defaultEmbeddingModel,
            defaultTranslationModel,
            ParseRequestTimeoutSeconds(requestTimeoutValue),
            ParseEmbeddingBatchSize(embeddingBatchSizeValue),
            ParseEmbeddingNumCtx(embeddingNumCtxValue));
    }

    public async Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default)
    {
        string normalizedBaseUrl = NormalizeAndValidateBaseUrl(settings.OllamaBaseUrl);
        int normalizedTimeout = ValidateRequestTimeoutSeconds(settings.RequestTimeoutSeconds);
        string? defaultChatModel = NormalizeOptionalValue(settings.DefaultChatModel);
        string? defaultEmbeddingModel = NormalizeOptionalValue(settings.DefaultEmbeddingModel);
        string? defaultTranslationModel = NormalizeOptionalValue(settings.DefaultTranslationModel);
        int embeddingBatchSize = ValidateEmbeddingBatchSize(settings.EmbeddingBatchSize);
        int? embeddingNumCtx = ValidateEmbeddingNumCtx(settings.EmbeddingNumCtx);

        await settingsRepository.UpsertAsync(BaseUrlKey, normalizedBaseUrl, cancellationToken);
        await settingsRepository.UpsertAsync(RequestTimeoutSecondsKey, normalizedTimeout.ToString(), cancellationToken);
        await settingsRepository.UpsertAsync(EmbeddingBatchSizeKey, embeddingBatchSize.ToString(), cancellationToken);
        await settingsRepository.UpsertAsync(DefaultChatModelKey, defaultChatModel ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(DefaultEmbeddingModelKey, defaultEmbeddingModel ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(DefaultTranslationModelKey, defaultTranslationModel ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(EmbeddingNumCtxKey, embeddingNumCtx?.ToString() ?? string.Empty, cancellationToken);

        return new OllamaSettings(
            normalizedBaseUrl,
            defaultChatModel,
            defaultEmbeddingModel,
            defaultTranslationModel,
            normalizedTimeout,
            embeddingBatchSize,
            embeddingNumCtx);
    }

    public async Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeRequiredModelName(modelName);
        OllamaSettings current = await GetAsync(cancellationToken);

        if (!string.Equals(current.DefaultChatModel, normalizedName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(current.DefaultEmbeddingModel, normalizedName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(current.DefaultTranslationModel, normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await UpdateAsync(
            current with
            {
                DefaultChatModel = string.Equals(current.DefaultChatModel, normalizedName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : current.DefaultChatModel,
                DefaultEmbeddingModel = string.Equals(current.DefaultEmbeddingModel, normalizedName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : current.DefaultEmbeddingModel,
                DefaultTranslationModel = string.Equals(current.DefaultTranslationModel, normalizedName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : current.DefaultTranslationModel
            },
            cancellationToken);
    }

    internal static string NormalizeAndValidateBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidUrl,
                "Inserisci un URL valido per Ollama, ad esempio http://localhost:11434.");
        }

        string trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidUrl,
                "L'URL di Ollama non e valido. Usa un indirizzo completo come http://localhost:11434.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidUrl,
                "L'URL di Ollama deve iniziare con http:// oppure https://.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidUrl,
                "L'URL di Ollama deve includere un host valido.");
        }

        UriBuilder builder = new(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    internal static int ValidateRequestTimeoutSeconds(int requestTimeoutSeconds)
    {
        if (requestTimeoutSeconds < MinRequestTimeoutSeconds || requestTimeoutSeconds > MaxRequestTimeoutSeconds)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                $"Il timeout deve essere compreso tra {MinRequestTimeoutSeconds} e {MaxRequestTimeoutSeconds} secondi.");
        }

        return requestTimeoutSeconds;
    }

    internal static int ValidateEmbeddingBatchSize(int embeddingBatchSize)
    {
        if (embeddingBatchSize < MinEmbeddingBatchSize || embeddingBatchSize > MaxEmbeddingBatchSize)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                $"La dimensione batch embedding deve essere compresa tra {MinEmbeddingBatchSize} e {MaxEmbeddingBatchSize} chunk.");
        }

        return embeddingBatchSize;
    }

    internal static string NormalizeRequiredModelName(string? modelName)
    {
        string? normalized = NormalizeOptionalValue(modelName);
        if (normalized is null)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "Specifica il nome del modello Ollama.");
        }

        return normalized;
    }

    private static int ParseRequestTimeoutSeconds(string? requestTimeoutValue)
    {
        return int.TryParse(requestTimeoutValue, out int parsed)
            && parsed >= MinRequestTimeoutSeconds
            && parsed <= MaxRequestTimeoutSeconds
            ? parsed
            : DefaultRequestTimeoutSeconds;
    }

    private static int ParseEmbeddingBatchSize(string? embeddingBatchSizeValue)
    {
        return int.TryParse(embeddingBatchSizeValue, out int parsed)
            && parsed >= MinEmbeddingBatchSize
            && parsed <= MaxEmbeddingBatchSize
            ? parsed
            : DefaultEmbeddingBatchSize;
    }

    internal static int? ValidateEmbeddingNumCtx(int? embeddingNumCtx)
    {
        if (embeddingNumCtx is null) return null;

        if (embeddingNumCtx.Value < MinEmbeddingNumCtx || embeddingNumCtx.Value > MaxEmbeddingNumCtx)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                $"La finestra di contesto embedding deve essere compresa tra {MinEmbeddingNumCtx} e {MaxEmbeddingNumCtx} token, oppure lasciata su Automatico.");
        }

        return embeddingNumCtx.Value;
    }

    private static int? ParseEmbeddingNumCtx(string? embeddingNumCtxValue)
    {
        if (string.IsNullOrWhiteSpace(embeddingNumCtxValue)) return null;
        return int.TryParse(embeddingNumCtxValue, out int parsed)
            && parsed >= MinEmbeddingNumCtx
            && parsed <= MaxEmbeddingNumCtx
            ? parsed
            : null;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

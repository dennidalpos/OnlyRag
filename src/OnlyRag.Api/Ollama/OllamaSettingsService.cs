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
    private const string DefaultCodingModelKey = "ollama.defaultCodingModel";
    private const string RequestTimeoutSecondsKey = "ollama.requestTimeoutSeconds";
    private const string EmbeddingBatchSizeKey = "ollama.embeddingBatchSize";
    private const string EmbeddingNumCtxKey = "ollama.embeddingNumCtx";
    private const string ChatNumCtxKey = "ollama.chatNumCtx";
    private const string TranslationNumCtxKey = "ollama.translationNumCtx";
    private const string CodingNumCtxKey = "ollama.codingNumCtx";
    private const string TrustNonLocalEndpointKey = "ollama.trustNonLocalEndpoint";
    private const int MinNumCtx = 64;
    private const int MaxNumCtx = 131072;

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
        string? defaultCodingModel = NormalizeOptionalValue(
            await settingsRepository.GetValueAsync(DefaultCodingModelKey, cancellationToken));
        string? requestTimeoutValue = await settingsRepository.GetValueAsync(RequestTimeoutSecondsKey, cancellationToken);
        string? embeddingBatchSizeValue = await settingsRepository.GetValueAsync(EmbeddingBatchSizeKey, cancellationToken);
        string? embeddingNumCtxValue = await settingsRepository.GetValueAsync(EmbeddingNumCtxKey, cancellationToken);
        string? chatNumCtxValue = await settingsRepository.GetValueAsync(ChatNumCtxKey, cancellationToken);
        string? translationNumCtxValue = await settingsRepository.GetValueAsync(TranslationNumCtxKey, cancellationToken);
        string? codingNumCtxValue = await settingsRepository.GetValueAsync(CodingNumCtxKey, cancellationToken);
        string? trustNonLocalEndpointValue = await settingsRepository.GetValueAsync(TrustNonLocalEndpointKey, cancellationToken);

        return new OllamaSettings(
            baseUrl,
            defaultChatModel,
            defaultEmbeddingModel,
            defaultTranslationModel,
            ParseRequestTimeoutSeconds(requestTimeoutValue),
            ParseEmbeddingBatchSize(embeddingBatchSizeValue),
            defaultCodingModel,
            ParseNumCtx(embeddingNumCtxValue),
            ParseNumCtx(chatNumCtxValue),
            ParseNumCtx(translationNumCtxValue),
            ParseNumCtx(codingNumCtxValue),
            ParseBoolean(trustNonLocalEndpointValue));
    }

    public async Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default)
    {
        string normalizedBaseUrl = NormalizeAndValidateBaseUrl(settings.OllamaBaseUrl, settings.TrustNonLocalEndpoint);
        int normalizedTimeout = ValidateRequestTimeoutSeconds(settings.RequestTimeoutSeconds);
        string? defaultChatModel = NormalizeOptionalValue(settings.DefaultChatModel);
        string? defaultEmbeddingModel = NormalizeOptionalValue(settings.DefaultEmbeddingModel);
        string? defaultTranslationModel = NormalizeOptionalValue(settings.DefaultTranslationModel);
        string? defaultCodingModel = NormalizeOptionalValue(settings.DefaultCodingModel);
        int embeddingBatchSize = ValidateEmbeddingBatchSize(settings.EmbeddingBatchSize);
        int? embeddingNumCtx = ValidateEmbeddingNumCtx(settings.EmbeddingNumCtx);
        int? chatNumCtx = ValidateNumCtx(settings.ChatNumCtx, "chat");
        int? translationNumCtx = ValidateNumCtx(settings.TranslationNumCtx, "traduzione");
        int? codingNumCtx = ValidateNumCtx(settings.CodingNumCtx, "coding");

        await settingsRepository.UpsertAsync(BaseUrlKey, normalizedBaseUrl, cancellationToken);
        await settingsRepository.UpsertAsync(RequestTimeoutSecondsKey, normalizedTimeout.ToString(), cancellationToken);
        await settingsRepository.UpsertAsync(EmbeddingBatchSizeKey, embeddingBatchSize.ToString(), cancellationToken);
        await settingsRepository.UpsertAsync(DefaultChatModelKey, defaultChatModel ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(DefaultEmbeddingModelKey, defaultEmbeddingModel ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(DefaultTranslationModelKey, defaultTranslationModel ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(DefaultCodingModelKey, defaultCodingModel ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(EmbeddingNumCtxKey, embeddingNumCtx?.ToString() ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(ChatNumCtxKey, chatNumCtx?.ToString() ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(TranslationNumCtxKey, translationNumCtx?.ToString() ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(CodingNumCtxKey, codingNumCtx?.ToString() ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(
            TrustNonLocalEndpointKey,
            settings.TrustNonLocalEndpoint ? bool.TrueString : bool.FalseString,
            cancellationToken);

        return new OllamaSettings(
            normalizedBaseUrl,
            defaultChatModel,
            defaultEmbeddingModel,
            defaultTranslationModel,
            normalizedTimeout,
            embeddingBatchSize,
            defaultCodingModel,
            embeddingNumCtx,
            chatNumCtx,
            translationNumCtx,
            codingNumCtx,
            settings.TrustNonLocalEndpoint);
    }

    public async Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeRequiredModelName(modelName);
        OllamaSettings current = await GetAsync(cancellationToken);

        if (!string.Equals(current.DefaultChatModel, normalizedName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(current.DefaultEmbeddingModel, normalizedName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(current.DefaultTranslationModel, normalizedName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(current.DefaultCodingModel, normalizedName, StringComparison.OrdinalIgnoreCase))
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
                    : current.DefaultTranslationModel,
                DefaultCodingModel = string.Equals(current.DefaultCodingModel, normalizedName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : current.DefaultCodingModel
            },
            cancellationToken);
    }

    internal static string NormalizeAndValidateBaseUrl(string? baseUrl)
    {
        return NormalizeAndValidateBaseUrl(baseUrl, trustNonLocalEndpoint: false);
    }

    internal static string NormalizeAndValidateBaseUrl(string? baseUrl, bool trustNonLocalEndpoint)
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

        if (!string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidUrl,
                "L'URL di Ollama non deve includere query string o frammenti.");
        }

        if (!uri.IsLoopback && !trustNonLocalEndpoint)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidUrl,
                "Gli endpoint Ollama non locali richiedono conferma esplicita perche chat, embedding e traduzione inviano testo al servizio configurato.");
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
        return ValidateNumCtx(embeddingNumCtx, "embedding");
    }

    internal static int? ValidateNumCtx(int? numCtx, string scope)
    {
        if (numCtx is null) return null;

        if (numCtx.Value < MinNumCtx || numCtx.Value > MaxNumCtx)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                $"La finestra di contesto {scope} deve essere compresa tra {MinNumCtx} e {MaxNumCtx} token, oppure lasciata su Automatico.");
        }

        return numCtx.Value;
    }

    private static int? ParseNumCtx(string? numCtxValue)
    {
        if (string.IsNullOrWhiteSpace(numCtxValue)) return null;
        return int.TryParse(numCtxValue, out int parsed)
            && parsed >= MinNumCtx
            && parsed <= MaxNumCtx
            ? parsed
            : null;
    }

    private static bool ParseBoolean(string? value)
    {
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

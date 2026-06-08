using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Images;

internal sealed class ImageGenerationSettingsService : IImageGenerationSettingsService
{
    private const string ProviderKey = "imageGeneration.provider";
    private const string Automatic1111BaseUrlKey = "imageGeneration.automatic1111BaseUrl";
    private const string ComfyUiBaseUrlKey = "imageGeneration.comfyUiBaseUrl";
    private const string RequestTimeoutSecondsKey = "imageGeneration.requestTimeoutSeconds";
    private const string TrustNonLocalEndpointKey = "imageGeneration.trustNonLocalEndpoint";
    private const string Automatic1111ModelKey = "imageGeneration.automatic1111Model";
    private const string ComfyUiWorkflowJsonKey = "imageGeneration.comfyUiWorkflowJson";
    private const int DefaultRequestTimeoutSeconds = 300;
    private const int MinRequestTimeoutSeconds = 10;
    private const int MaxRequestTimeoutSeconds = 1800;

    private readonly ISettingsRepository settingsRepository;

    public ImageGenerationSettingsService(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<ImageGenerationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string provider = ImageGenerationProviderNames.Normalize(
            await settingsRepository.GetValueAsync(ProviderKey, cancellationToken));
        string automatic1111BaseUrl = await settingsRepository.GetValueAsync(Automatic1111BaseUrlKey, cancellationToken)
            ?? "http://127.0.0.1:7860";
        string comfyUiBaseUrl = await settingsRepository.GetValueAsync(ComfyUiBaseUrlKey, cancellationToken)
            ?? "http://127.0.0.1:8188";
        string? timeoutValue = await settingsRepository.GetValueAsync(RequestTimeoutSecondsKey, cancellationToken);
        string? trustValue = await settingsRepository.GetValueAsync(TrustNonLocalEndpointKey, cancellationToken);

        return new ImageGenerationSettings(
            provider,
            automatic1111BaseUrl,
            comfyUiBaseUrl,
            ParseRequestTimeoutSeconds(timeoutValue),
            bool.TryParse(trustValue, out bool trust) && trust,
            NormalizeOptional(await settingsRepository.GetValueAsync(Automatic1111ModelKey, cancellationToken)),
            NormalizeOptional(await settingsRepository.GetValueAsync(ComfyUiWorkflowJsonKey, cancellationToken)));
    }

    public async Task<ImageGenerationSettings> UpdateAsync(
        ImageGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        string provider = ImageGenerationProviderNames.Normalize(settings.Provider);
        string automatic1111BaseUrl = NormalizeAndValidateBaseUrl(
            settings.Automatic1111BaseUrl,
            settings.TrustNonLocalEndpoint,
            "Automatic1111");
        string comfyUiBaseUrl = NormalizeAndValidateBaseUrl(
            settings.ComfyUiBaseUrl,
            settings.TrustNonLocalEndpoint,
            "ComfyUI");
        int timeout = ValidateRequestTimeoutSeconds(settings.RequestTimeoutSeconds);
        string? automatic1111Model = NormalizeOptional(settings.Automatic1111Model);
        string? comfyUiWorkflowJson = NormalizeOptional(settings.ComfyUiWorkflowJson);

        await settingsRepository.UpsertAsync(ProviderKey, provider, cancellationToken);
        await settingsRepository.UpsertAsync(Automatic1111BaseUrlKey, automatic1111BaseUrl, cancellationToken);
        await settingsRepository.UpsertAsync(ComfyUiBaseUrlKey, comfyUiBaseUrl, cancellationToken);
        await settingsRepository.UpsertAsync(RequestTimeoutSecondsKey, timeout.ToString(), cancellationToken);
        await settingsRepository.UpsertAsync(
            TrustNonLocalEndpointKey,
            settings.TrustNonLocalEndpoint ? bool.TrueString : bool.FalseString,
            cancellationToken);
        await settingsRepository.UpsertAsync(Automatic1111ModelKey, automatic1111Model ?? string.Empty, cancellationToken);
        await settingsRepository.UpsertAsync(ComfyUiWorkflowJsonKey, comfyUiWorkflowJson ?? string.Empty, cancellationToken);

        return new ImageGenerationSettings(
            provider,
            automatic1111BaseUrl,
            comfyUiBaseUrl,
            timeout,
            settings.TrustNonLocalEndpoint,
            automatic1111Model,
            comfyUiWorkflowJson);
    }

    public static string ResolveBaseUrl(ImageGenerationSettings settings, string provider)
    {
        return provider == ImageGenerationProviderNames.ComfyUi
            ? settings.ComfyUiBaseUrl
            : settings.Automatic1111BaseUrl;
    }

    public static string NormalizeAndValidateBaseUrl(
        string? baseUrl,
        bool trustNonLocalEndpoint,
        string providerLabel)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                $"Inserisci un URL valido per {providerLabel}.");
        }

        string trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                $"L'URL di {providerLabel} non e valido.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                $"L'URL di {providerLabel} deve iniziare con http:// oppure https://.");
        }

        if (!string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                $"L'URL di {providerLabel} non deve includere query string o frammenti.");
        }

        if (!uri.IsLoopback && !trustNonLocalEndpoint)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                $"Gli endpoint immagini non locali richiedono conferma esplicita perche i prompt vengono inviati a {providerLabel}.");
        }

        UriBuilder builder = new(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    public static int ValidateRequestTimeoutSeconds(int requestTimeoutSeconds)
    {
        if (requestTimeoutSeconds < MinRequestTimeoutSeconds || requestTimeoutSeconds > MaxRequestTimeoutSeconds)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                $"Il timeout immagini deve essere compreso tra {MinRequestTimeoutSeconds} e {MaxRequestTimeoutSeconds} secondi.");
        }

        return requestTimeoutSeconds;
    }

    private static int ParseRequestTimeoutSeconds(string? value)
    {
        return int.TryParse(value, out int parsed)
            && parsed >= MinRequestTimeoutSeconds
            && parsed <= MaxRequestTimeoutSeconds
            ? parsed
            : DefaultRequestTimeoutSeconds;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}


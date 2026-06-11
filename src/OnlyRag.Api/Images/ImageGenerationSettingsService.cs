using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Images;

internal sealed class ImageGenerationSettingsService : IImageGenerationSettingsService
{
    private const string SelectedModelIdKey = "imageGeneration.selectedModelId";
    private const string RequestTimeoutSecondsKey = "imageGeneration.requestTimeoutSeconds";
    private const string PreferGpuKey = "imageGeneration.preferGpu";
    private const string ActiveExecutionProviderKey = "imageGeneration.activeExecutionProvider";
    private const string DefaultSelectedModelId = ImageModelCatalog.DefaultModelId;
    private const int DefaultRequestTimeoutSeconds = 300;
    private const int MinRequestTimeoutSeconds = 10;
    private const int MaxRequestTimeoutSeconds = 1800;

    private readonly ISettingsRepository settingsRepository;
    private readonly ImageModelCatalogStore modelCatalog;

    public ImageGenerationSettingsService(
        ISettingsRepository settingsRepository,
        ImageModelCatalogStore modelCatalog)
    {
        this.settingsRepository = settingsRepository;
        this.modelCatalog = modelCatalog;
    }

    public async Task<ImageGenerationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string selectedModelId = await NormalizeModelIdAsync(
            await settingsRepository.GetValueAsync(SelectedModelIdKey, cancellationToken),
            cancellationToken);
        string? timeoutValue = await settingsRepository.GetValueAsync(RequestTimeoutSecondsKey, cancellationToken);
        string? preferGpuValue = await settingsRepository.GetValueAsync(PreferGpuKey, cancellationToken);
        string activeExecutionProvider = NormalizeExecutionProvider(
            await settingsRepository.GetValueAsync(ActiveExecutionProviderKey, cancellationToken));

        return new ImageGenerationSettings(
            selectedModelId,
            ParseRequestTimeoutSeconds(timeoutValue),
            !bool.TryParse(preferGpuValue, out bool preferGpu) || preferGpu,
            activeExecutionProvider);
    }

    public async Task<ImageGenerationSettings> UpdateAsync(
        ImageGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        string selectedModelId = await NormalizeModelIdAsync(settings.SelectedModelId, cancellationToken);
        int timeout = ValidateRequestTimeoutSeconds(settings.RequestTimeoutSeconds);
        string activeExecutionProvider = NormalizeExecutionProvider(settings.ActiveExecutionProvider);

        await settingsRepository.UpsertAsync(SelectedModelIdKey, selectedModelId, cancellationToken);
        await settingsRepository.UpsertAsync(RequestTimeoutSecondsKey, timeout.ToString(), cancellationToken);
        await settingsRepository.UpsertAsync(PreferGpuKey, settings.PreferGpu ? bool.TrueString : bool.FalseString, cancellationToken);
        await settingsRepository.UpsertAsync(ActiveExecutionProviderKey, activeExecutionProvider, cancellationToken);

        return new ImageGenerationSettings(
            selectedModelId,
            timeout,
            settings.PreferGpu,
            activeExecutionProvider);
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

    private async Task<string> NormalizeModelIdAsync(string? value, CancellationToken cancellationToken)
    {
        string modelId = string.IsNullOrWhiteSpace(value) ? DefaultSelectedModelId : value.Trim();
        if (!await modelCatalog.ContainsAsync(modelId, cancellationToken))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                "Il modello immagini selezionato non fa parte del catalogo integrato.");
        }

        return modelId;
    }

    private static string NormalizeExecutionProvider(string? value)
    {
        string provider = string.IsNullOrWhiteSpace(value) ? "CPU" : value.Trim();
        return provider.Equals("DirectML", StringComparison.OrdinalIgnoreCase) ? "DirectML" : "CPU";
    }
}

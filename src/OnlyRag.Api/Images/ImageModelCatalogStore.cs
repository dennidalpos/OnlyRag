using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Images;

internal sealed class ImageModelCatalogStore
{
    private const string CatalogOverridesKey = "imageGeneration.modelCatalogOverrides";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISettingsRepository settingsRepository;

    public ImageModelCatalogStore(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<IReadOnlyList<ImageModelCatalogEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, ImageModelCatalogEntry> models = ImageModelCatalog
            .ListDefaults()
            .ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);

        foreach (ImageModelCatalogEntry saved in await ReadSavedAsync(cancellationToken))
        {
            bool isBuiltIn = ImageModelCatalog.IsBuiltIn(saved.Id);
            models[saved.Id] = saved with { IsBuiltIn = isBuiltIn };
        }

        return models.Values
            .OrderBy(model => model.IsBuiltIn ? 0 : 1)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<bool> ContainsAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return (await ListAsync(cancellationToken)).Any(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ImageModelCatalogEntry> GetAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return (await ListAsync(cancellationToken)).FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ImageGenerationException(
                ImageGenerationErrorKind.NotFound,
                "Modello immagini non presente nel catalogo integrato.");
    }

    public async Task<ImageModelCatalogEntry> UpsertAsync(
        ImageModelCatalogEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ImageModelCatalogEntry normalized = Normalize(request);
        List<ImageModelCatalogEntry> saved = await ReadSavedAsync(cancellationToken);
        saved.RemoveAll(model => string.Equals(model.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        saved.Add(normalized);
        await SaveAsync(saved, cancellationToken);
        return normalized;
    }

    public async Task<ImageModelCatalogEntry> ResetOrDeleteAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        List<ImageModelCatalogEntry> saved = await ReadSavedAsync(cancellationToken);
        bool removed = saved.RemoveAll(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed && !ImageModelCatalog.IsBuiltIn(modelId))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.NotFound,
                "Modello immagini non presente nel catalogo integrato.");
        }

        await SaveAsync(saved, cancellationToken);
        return ImageModelCatalog.GetDefault(modelId)
            ?? throw new ImageGenerationException(
                ImageGenerationErrorKind.NotFound,
                "Modello immagini rimosso dal catalogo.");
    }

    private async Task<List<ImageModelCatalogEntry>> ReadSavedAsync(CancellationToken cancellationToken)
    {
        string? raw = await settingsRepository.GetValueAsync(CatalogOverridesKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ImageModelCatalogEntry>>(raw, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveAsync(List<ImageModelCatalogEntry> models, CancellationToken cancellationToken)
    {
        string raw = JsonSerializer.Serialize(models, JsonOptions);
        await settingsRepository.UpsertAsync(CatalogOverridesKey, raw, cancellationToken);
    }

    private static ImageModelCatalogEntry Normalize(ImageModelCatalogEntryRequest request)
    {
        string id = NormalizeRequired(request.Id, "Inserisci un id modello immagini.");
        if (!id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "L'id modello puo contenere solo lettere, numeri, punto, trattino e underscore.");
        }

        string downloadUrl = NormalizeRequired(request.DownloadUrl, "Inserisci un URL di download modello.");
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https" or "file"))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "L'URL modello deve essere assoluto e usare http, https o file.");
        }

        string[] requiredFiles = request.RequiredFiles
            .Select(file => file.Trim().Replace('\\', '/'))
            .Where(file => file.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requiredFiles.Length == 0)
        {
            requiredFiles = [ImageModelCatalog.RequiredModelFileName];
        }

        foreach (string requiredFile in requiredFiles)
        {
            if (Path.IsPathRooted(requiredFile) || requiredFile.Contains("..", StringComparison.Ordinal))
            {
                throw new ImageGenerationException(
                    ImageGenerationErrorKind.InvalidRequest,
                    "I file richiesti del modello devono essere percorsi relativi validi.");
            }
        }

        if (ImageModelCatalog.GetDefault(id) is { } defaultModel)
        {
            requiredFiles = requiredFiles
                .Concat(defaultModel.RequiredFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        string sha256 = request.Sha256.Trim();
        if (sha256.Length is not 0 and not 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Lo SHA256 modello deve essere vuoto oppure contenere 64 caratteri esadecimali.");
        }

        return new ImageModelCatalogEntry(
            id,
            NormalizeRequired(request.DisplayName, "Inserisci un nome modello immagini."),
            NormalizeRequired(request.RecommendedProfile, "Inserisci un profilo modello immagini."),
            downloadUrl,
            NormalizeRequired(request.LicenseLabel, "Inserisci una licenza modello immagini."),
            Math.Max(0, request.ExpectedSizeBytes),
            requiredFiles,
            sha256.ToLowerInvariant(),
            ImageModelCatalog.IsBuiltIn(id));
    }

    private static string NormalizeRequired(string value, string errorMessage)
    {
        string normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ImageGenerationException(ImageGenerationErrorKind.InvalidRequest, errorMessage);
        }

        return normalized;
    }
}

using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Images;

internal sealed class ImageGenerationService
{
    private const string GeneratedImagesRelativeRoot = "images/generated";

    private readonly InProcessBackendDescriptor descriptor;
    private readonly IImageGenerationSettingsService settingsService;
    private readonly IEnumerable<IImageGenerationClient> clients;
    private readonly IGeneratedImageRepository images;

    public ImageGenerationService(
        InProcessBackendDescriptor descriptor,
        IImageGenerationSettingsService settingsService,
        IEnumerable<IImageGenerationClient> clients,
        IGeneratedImageRepository images)
    {
        this.descriptor = descriptor;
        this.settingsService = settingsService;
        this.clients = clients;
        this.images = images;
    }

    public async Task<IReadOnlyList<ImageGenerationProviderStatus>> GetProviderStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        ImageGenerationSettings settings = await settingsService.GetAsync(cancellationToken);
        List<ImageGenerationProviderStatus> statuses = [];
        foreach (IImageGenerationClient client in clients)
        {
            statuses.Add(await client.GetStatusAsync(settings, cancellationToken));
        }

        return statuses
            .OrderBy(status => status.Provider, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ImageGenerationSettings settings = await settingsService.GetAsync(cancellationToken);
        ImageGenerationRequest normalized = ImageGenerationRequestValidator.Normalize(
            string.IsNullOrWhiteSpace(request.Provider)
                ? request with { Provider = settings.Provider }
                : request);
        IImageGenerationClient client = ResolveClient(normalized.Provider);
        IReadOnlyList<ImageGenerationBinary> generated = await client.GenerateAsync(settings, normalized, cancellationToken);
        if (generated.Count == 0)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Il provider immagini non ha restituito immagini.");
        }

        string generatedRoot = GetGeneratedRoot();
        Directory.CreateDirectory(generatedRoot);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<GeneratedImage> savedImages = [];
        foreach (ImageGenerationBinary image in generated)
        {
            string fileName = $"{Guid.NewGuid():N}{image.FileExtension}";
            string relativePath = Path.Combine(GeneratedImagesRelativeRoot, fileName).Replace('\\', '/');
            string absolutePath = ResolveGeneratedPath(relativePath);
            await File.WriteAllBytesAsync(absolutePath, image.Content, cancellationToken);
            GeneratedImage saved = await images.CreateAsync(
                new GeneratedImage(
                    0,
                    normalized.Provider,
                    normalized.Prompt,
                    normalized.NegativePrompt,
                    normalized.Model,
                    normalized.Width,
                    normalized.Height,
                    normalized.Steps,
                    normalized.BatchSize,
                    normalized.Seed,
                    fileName,
                    image.MimeType,
                    image.Content.LongLength,
                    now),
                relativePath,
                cancellationToken);
            savedImages.Add(saved);
        }

        return new ImageGenerationResponse(
            normalized.Provider,
            savedImages.Count == 1 ? "Immagine generata." : $"{savedImages.Count} immagini generate.",
            savedImages);
    }

    public Task<IReadOnlyList<GeneratedImage>> ListAsync(CancellationToken cancellationToken = default)
    {
        return images.ListAsync(cancellationToken: cancellationToken);
    }

    public async Task<(GeneratedImage Image, string AbsolutePath)?> GetFileAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        (GeneratedImage Image, string RelativePath)? record = await images.GetWithPathAsync(id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        string absolutePath = ResolveGeneratedPath(record.Value.RelativePath);
        return File.Exists(absolutePath) ? (record.Value.Image, absolutePath) : null;
    }

    private IImageGenerationClient ResolveClient(string provider)
    {
        string normalizedProvider = ImageGenerationProviderNames.Normalize(provider);
        IImageGenerationClient? client = clients.FirstOrDefault(candidate =>
            string.Equals(candidate.Provider, normalizedProvider, StringComparison.OrdinalIgnoreCase));
        return client
            ?? throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Provider immagini non configurato.");
    }

    private string GetGeneratedRoot()
    {
        return Path.Combine(descriptor.StoragePaths.DataRoot, "images", "generated");
    }

    private string ResolveGeneratedPath(string relativePath)
    {
        string root = Path.GetFullPath(GetGeneratedRoot());
        string absolutePath = Path.GetFullPath(Path.Combine(descriptor.StoragePaths.DataRoot, relativePath));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Percorso immagine generata non valido.");
        }

        return absolutePath;
    }
}


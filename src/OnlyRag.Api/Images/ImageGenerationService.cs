using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Images;

internal sealed class ImageGenerationService
{
    private const string GeneratedImagesRelativeRoot = "images/generated";

    private readonly InProcessBackendDescriptor descriptor;
    private readonly IImageGenerationSettingsService settingsService;
    private readonly ImageModelManager modelManager;
    private readonly IGeneratedImageRepository images;

    public ImageGenerationService(
        InProcessBackendDescriptor descriptor,
        IImageGenerationSettingsService settingsService,
        ImageModelManager modelManager,
        IGeneratedImageRepository images)
    {
        this.descriptor = descriptor;
        this.settingsService = settingsService;
        this.modelManager = modelManager;
        this.images = images;
    }

    public async Task<ImageGenerationRuntimeStatus> GetRuntimeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        ImageGenerationSettings settings = await settingsService.GetAsync(cancellationToken);
        ImageModelLocalState state = await modelManager.GetStateAsync(settings.SelectedModelId, cancellationToken);
        return new ImageGenerationRuntimeStatus(
            state.IsVerified ? "Ready" : state.State,
            state.IsVerified,
            settings.ActiveExecutionProvider,
            state.IsVerified
                ? $"Provider integrato pronto con {settings.ActiveExecutionProvider}."
                : "Scarica e verifica un modello integrato prima di generare immagini.",
            state.IsVerified ? null : state.VerificationError);
    }

    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ImageGenerationSettings settings = await settingsService.GetAsync(cancellationToken);
        ImageGenerationRequest normalized = ImageGenerationRequestValidator.Normalize(
            string.IsNullOrWhiteSpace(request.ModelId)
                ? request with { ModelId = settings.SelectedModelId }
                : request);
        _ = await modelManager.GetVerifiedModelFilePathAsync(normalized.ModelId ?? settings.SelectedModelId, cancellationToken);
        IReadOnlyList<ImageGenerationBinary> generated = GenerateIntegratedImages(normalized);
        if (generated.Count == 0)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Il generatore immagini integrato non ha restituito immagini.");
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
                    ImageGenerationProviderNames.Integrated,
                    normalized.Prompt,
                    normalized.NegativePrompt,
                    normalized.ModelId,
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
            ImageGenerationProviderNames.Integrated,
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

    private static IReadOnlyList<ImageGenerationBinary> GenerateIntegratedImages(ImageGenerationRequest request)
    {
        List<ImageGenerationBinary> images = [];
        for (int index = 0; index < request.BatchSize; index++)
        {
            long? imageSeed = request.Seed is null ? null : request.Seed + index;
            images.Add(IntegratedImageGenerator.GeneratePng(
                request.Prompt,
                request.NegativePrompt,
                request.Width,
                request.Height,
                imageSeed));
        }

        return images;
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

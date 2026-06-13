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
    private readonly IImageGenerationEngine engine;

    public ImageGenerationService(
        InProcessBackendDescriptor descriptor,
        IImageGenerationSettingsService settingsService,
        ImageModelManager modelManager,
        IGeneratedImageRepository images,
        IImageGenerationEngine engine)
    {
        this.descriptor = descriptor;
        this.settingsService = settingsService;
        this.modelManager = modelManager;
        this.images = images;
        this.engine = engine;
    }

    public async Task<ImageGenerationRuntimeStatus> GetRuntimeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        ImageGenerationSettings settings = await settingsService.GetAsync(cancellationToken);
        ImageModelLocalState state = await modelManager.GetStateAsync(settings.SelectedModelId, cancellationToken);
        ImageGenerationEngineStatus engineStatus = engine.GetStatus();
        string preferredProvider = settings.PreferGpu ? "DirectML" : "CPU";
        string activeProvider = engineStatus.IsInitialized
            ? engineStatus.ActiveExecutionProvider
            : preferredProvider;
        string? fallbackReason = settings.PreferGpu
            ? engineStatus.FallbackReason
            : "DirectML disabilitato nelle impostazioni immagini.";
        return new ImageGenerationRuntimeStatus(
            state.IsVerified ? "Ready" : state.State,
            state.IsVerified,
            activeProvider,
            state.IsVerified
                ? $"Provider integrato pronto con {activeProvider}."
                : "Scarica e verifica un modello integrato prima di generare immagini.",
            state.IsVerified ? null : state.VerificationError,
            preferredProvider,
            state.State,
            fallbackReason);
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
        string modelId = normalized.ModelId ?? settings.SelectedModelId;
        _ = await modelManager.GetVerifiedModelFilePathAsync(modelId, cancellationToken);
        string modelDirectory = modelManager.GetModelDirectory(modelId);
        ImageGenerationEngineResult engineResult;
        try
        {
            engineResult = await engine.GenerateAsync(
                normalized,
                modelDirectory,
                settings.PreferGpu,
                cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.ModelNotReady,
                "Il modello immagini locale e incompleto. Elimina il download del modello e scaricalo di nuovo.",
                ex);
        }
        catch (TimeoutException ex)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Timeout,
                "La generazione immagini non ha completato entro il tempo configurato. Aumenta il timeout o usa un preset piu veloce.",
                ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Timeout,
                "La generazione immagini e stata interrotta per timeout interno del motore.",
                ex);
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex) when (IsEngineConfigurationException(ex))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                $"Il motore immagini integrato non puo usare il modello selezionato: {ex.Message}",
                ex);
        }

        IReadOnlyList<ImageGenerationBinary> generated = engineResult.Images;
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

    public async Task<GeneratedImage?> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        (GeneratedImage Image, string RelativePath)? record = await images.GetWithPathAsync(id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        string absolutePath = ResolveGeneratedPath(record.Value.RelativePath);
        GeneratedImage? deleted = await images.DeleteAsync(id, cancellationToken);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return deleted;
    }

    public string GetGeneratedRoot()
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

    private static bool IsEngineConfigurationException(Exception exception)
    {
        return exception is InvalidOperationException
            or NotSupportedException
            or DllNotFoundException
            or EntryPointNotFoundException
            or IOException;
    }
}

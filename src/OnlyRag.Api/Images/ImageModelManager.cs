using System.Security.Cryptography;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal sealed class ImageModelManager
{
    private readonly InProcessBackendDescriptor descriptor;
    private readonly HttpClient httpClient;
    private readonly ImageModelCatalogStore modelCatalog;
    private readonly HashSet<string> activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private static readonly string[] SupportedPipelineClasses =
    [
        "StableDiffusionXLPipeline",
        "OnnxStableDiffusionXLPipeline",
        "ORTStableDiffusionXLPipeline"
    ];

    private sealed record RemoteFileInfo(long? ContentLength, string? Sha256);

    private sealed record ModelSnapshotFile(string RelativePath, long? SizeBytes, string? Sha256);

    public ImageModelManager(
        InProcessBackendDescriptor descriptor,
        HttpClient httpClient,
        ImageModelCatalogStore modelCatalog)
    {
        this.descriptor = descriptor;
        this.httpClient = httpClient;
        this.modelCatalog = modelCatalog;
    }

    public Task<IReadOnlyList<ImageModelCatalogEntry>> ListCatalogAsync(CancellationToken cancellationToken = default)
    {
        return modelCatalog.ListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ImageModelLocalState>> ListStatesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ImageModelCatalogEntry> models = await modelCatalog.ListAsync(cancellationToken);
        List<ImageModelLocalState> states = [];
        foreach (ImageModelCatalogEntry model in models)
        {
            states.Add(GetState(model));
        }

        return states;
    }

    public async Task<ImageModelLocalState> GetStateAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ImageModelCatalogEntry model = await modelCatalog.GetAsync(modelId, cancellationToken);
        return GetState(model);
    }

    private ImageModelLocalState GetState(ImageModelCatalogEntry model)
    {
        string modelDirectory = GetModelDirectory(model.Id);
        long localSizeBytes = GetDirectorySize(modelDirectory);
        bool isDownloading;
        lock (gate)
        {
            isDownloading = activeDownloads.Contains(model.Id);
        }

        string modelPath = GetModelFilePath(model.Id);
        bool hasRequiredFiles = HasRequiredFiles(model, modelDirectory);
        if (!hasRequiredFiles)
        {
            if (ModelRequiresSingleOnnxFile(model) && IsPlaceholderModelFile(modelPath))
            {
                return new ImageModelLocalState(
                    model.Id,
                    "VerificationFailed",
                    IsDownloaded: true,
                    IsVerified: false,
                    localSizeBytes,
                    modelDirectory,
                    "Il file locale e un segnaposto tecnico e non contiene un modello immagini eseguibile.",
                    model.ExpectedSizeBytes,
                    CalculateRemainingDownloadBytes(model.ExpectedSizeBytes, localSizeBytes));
            }

            return new ImageModelLocalState(
                model.Id,
                isDownloading ? "Downloading" : "NotDownloaded",
                IsDownloaded: false,
                IsVerified: false,
                localSizeBytes,
                modelDirectory,
                isDownloading
                    ? null
                    : localSizeBytes > 0
                        ? "Il modello locale e incompleto. Elimina il download e scaricalo di nuovo."
                        : "Il modello non e ancora stato scaricato.",
                model.ExpectedSizeBytes,
                CalculateRemainingDownloadBytes(model.ExpectedSizeBytes, localSizeBytes));
        }

        if (ModelRequiresSingleOnnxFile(model) && IsPlaceholderModelFile(modelPath))
        {
            return new ImageModelLocalState(
                model.Id,
                "VerificationFailed",
                IsDownloaded: true,
                IsVerified: false,
                localSizeBytes,
                modelDirectory,
                "Il file locale e un segnaposto tecnico e non contiene un modello immagini eseguibile.",
                model.ExpectedSizeBytes,
                CalculateRemainingDownloadBytes(model.ExpectedSizeBytes, localSizeBytes));
        }

        string? snapshotVerificationError = GetSnapshotVerificationError(model, modelDirectory);
        if (snapshotVerificationError is not null)
        {
            return new ImageModelLocalState(
                model.Id,
                "VerificationFailed",
                IsDownloaded: true,
                IsVerified: false,
                localSizeBytes,
                modelDirectory,
                snapshotVerificationError,
                model.ExpectedSizeBytes,
                CalculateRemainingDownloadBytes(model.ExpectedSizeBytes, localSizeBytes));
        }

        if (string.IsNullOrWhiteSpace(model.Sha256))
        {
            return new ImageModelLocalState(
                model.Id,
                "Ready",
                IsDownloaded: true,
                IsVerified: true,
                localSizeBytes,
                modelDirectory,
                null,
                model.ExpectedSizeBytes,
                CalculateRemainingDownloadBytes(model.ExpectedSizeBytes, localSizeBytes));
        }

        bool hashMatches = File.Exists(modelPath)
            && ComputeSha256(modelPath).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
        return new ImageModelLocalState(
            model.Id,
            hashMatches ? "Verified" : "VerificationFailed",
            IsDownloaded: true,
            IsVerified: hashMatches,
            localSizeBytes,
            modelDirectory,
            hashMatches ? null : "Hash SHA256 non valido per il file modello locale.",
            model.ExpectedSizeBytes,
            CalculateRemainingDownloadBytes(model.ExpectedSizeBytes, localSizeBytes));
    }

    public async Task<ImageModelDownloadResponse> DownloadAsync(
        string modelId,
        ImageModelDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ConsentConfirmed)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Conferma dimensione, licenza e destinazione locale prima di scaricare il modello immagini.");
        }

        ImageModelCatalogEntry model = await modelCatalog.GetAsync(modelId, cancellationToken);
        lock (gate)
        {
            if (!activeDownloads.Add(model.Id))
            {
                return new ImageModelDownloadResponse(model.Id, "Downloading", "Download modello gia in corso.");
            }
        }

        try
        {
            Directory.CreateDirectory(GetModelDirectory(model.Id));
            await DownloadModelAsync(model, cancellationToken);
            ImageModelLocalState state = await GetStateAsync(model.Id, cancellationToken);
            if (!state.IsVerified && state.State != "Downloaded")
            {
                throw new ImageGenerationException(
                    ImageGenerationErrorKind.InvalidConfiguration,
                    state.VerificationError ?? "Verifica modello immagini non riuscita.");
            }

            return new ImageModelDownloadResponse(
                model.Id,
                state.State,
                state.IsVerified && !string.IsNullOrWhiteSpace(model.Sha256)
                    ? "Modello immagini scaricato e verificato."
                    : "Modello immagini scaricato e pronto.");
        }
        finally
        {
            lock (gate)
            {
                activeDownloads.Remove(model.Id);
            }
        }
    }

    public async Task<ImageModelDownloadResponse> CancelDownloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ImageModelCatalogEntry model = await modelCatalog.GetAsync(modelId, cancellationToken);
        bool removed;
        lock (gate)
        {
            removed = activeDownloads.Remove(model.Id);
        }

        if (!removed)
        {
            return new ImageModelDownloadResponse(
                model.Id,
                (await GetStateAsync(model.Id, cancellationToken)).State,
                "Nessun download modello attivo.");
        }

        return new ImageModelDownloadResponse(model.Id, "Cancelled", "Download modello annullato.");
    }

    public async Task<ImageModelDownloadResponse> DeleteAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ImageModelCatalogEntry model = await modelCatalog.GetAsync(modelId, cancellationToken);
        string modelDirectory = GetModelDirectory(model.Id);
        if (Directory.Exists(modelDirectory))
        {
            Directory.Delete(modelDirectory, recursive: true);
        }

        return new ImageModelDownloadResponse(model.Id, "NotDownloaded", "File modello rimossi.");
    }

    public async Task<string> GetVerifiedModelFilePathAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ImageModelLocalState state = await GetStateAsync(modelId, cancellationToken);
        if (!state.IsVerified)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.ModelNotReady,
                state.VerificationError ?? "Scarica e verifica il modello immagini prima di generare.");
        }

        return GetModelFilePath(modelId);
    }

    public string GetModelFilePath(string modelId)
    {
        return Path.Combine(GetModelDirectory(modelId), ImageModelCatalog.RequiredModelFileName);
    }

    public string GetModelDirectory(string modelId)
    {
        string root = Path.GetFullPath(descriptor.StoragePaths.ImageModelsDirectory);
        string modelDirectory = Path.GetFullPath(Path.Combine(root, modelId));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!modelDirectory.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Percorso modello immagini non valido.");
        }

        return modelDirectory;
    }

    private async Task DownloadModelAsync(
        ImageModelCatalogEntry model,
        CancellationToken cancellationToken)
    {
        Uri uri = new(model.DownloadUrl);
        if (IsHuggingFaceModelPage(uri, out string? repositoryId))
        {
            await DownloadHuggingFaceSnapshotAsync(repositoryId, GetModelDirectory(model.Id), cancellationToken);
            return;
        }

        string destinationPath = GetModelFilePath(model.Id);
        if (uri.Scheme == Uri.UriSchemeFile)
        {
            await DownloadFileIfNeededAsync(
                uri,
                destinationPath,
                NormalizeSha256OrNull(model.Sha256),
                model.ExpectedSizeBytes > 0 ? model.ExpectedSizeBytes : null,
                cancellationToken);
            return;
        }

        await DownloadFileIfNeededAsync(
            uri,
            destinationPath,
            NormalizeSha256OrNull(model.Sha256),
            model.ExpectedSizeBytes > 0 ? model.ExpectedSizeBytes : null,
            cancellationToken);
    }

    private async Task DownloadHuggingFaceSnapshotAsync(
        string repositoryId,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ModelSnapshotFile> snapshotFiles = await ListHuggingFaceSnapshotFilesAsync(repositoryId, cancellationToken);
        if (snapshotFiles.Count == 0)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Metadata modello Hugging Face senza file modello.");
        }

        foreach (ModelSnapshotFile snapshotFile in snapshotFiles)
        {
            string safeRelativePath = snapshotFile.RelativePath.Replace('\\', '/');
            string destinationPath = ResolveModelSnapshotPath(destinationDirectory, safeRelativePath);
            Uri downloadUri = CreateHuggingFaceResolveUri(repositoryId, safeRelativePath);
            await DownloadFileIfNeededAsync(
                downloadUri,
                destinationPath,
                snapshotFile.Sha256,
                snapshotFile.SizeBytes,
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ModelSnapshotFile>> ListHuggingFaceSnapshotFilesAsync(
        string repositoryId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage treeResponse = await httpClient.GetAsync(
            $"https://huggingface.co/api/models/{repositoryId}/tree/main?recursive=true",
            cancellationToken);
        if (!treeResponse.IsSuccessStatusCode)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Unreachable,
                $"Metadata modello Hugging Face non raggiungibili. HTTP {(int)treeResponse.StatusCode}.");
        }

        using JsonDocument metadata = JsonDocument.Parse(await treeResponse.Content.ReadAsStreamAsync(cancellationToken));
        if (metadata.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Metadata modello Hugging Face senza lista file.");
        }

        List<ModelSnapshotFile> files = [];
        foreach (JsonElement entry in metadata.RootElement.EnumerateArray())
        {
            string type = entry.TryGetProperty("type", out JsonElement typeElement)
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;
            if (!type.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? relativePath = entry.TryGetProperty("path", out JsonElement pathElement)
                ? pathElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long? sizeBytes = TryGetInt64(entry, "size");
            string? sha256 = null;
            if (entry.TryGetProperty("lfs", out JsonElement lfs) && lfs.ValueKind == JsonValueKind.Object)
            {
                sha256 = NormalizeSha256OrNull(
                    lfs.TryGetProperty("oid", out JsonElement oidElement)
                        ? oidElement.GetString() ?? string.Empty
                        : string.Empty);
                sizeBytes = TryGetInt64(lfs, "size") ?? sizeBytes;
            }

            files.Add(new ModelSnapshotFile(relativePath.Replace('\\', '/'), sizeBytes, sha256));
        }

        return files;
    }

    private async Task DownloadFileIfNeededAsync(
        Uri uri,
        string destinationPath,
        string? expectedSha256,
        long? expectedSizeBytes,
        CancellationToken cancellationToken)
    {
        RemoteFileInfo remoteInfo = await ProbeRemoteFileAsync(uri, cancellationToken);
        string? sha256 = expectedSha256 ?? remoteInfo.Sha256;
        long? sizeBytes = expectedSizeBytes ?? remoteInfo.ContentLength;
        if (await ExistingFileMatchesAsync(destinationPath, sha256, sizeBytes, cancellationToken))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        string tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await DownloadFileCoreAsync(uri, tempPath, cancellationToken);
            await VerifyDownloadedFileAsync(tempPath, sha256, sizeBytes, cancellationToken);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private async Task<RemoteFileInfo> ProbeRemoteFileAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme == Uri.UriSchemeFile)
        {
            FileInfo source = new(uri.LocalPath);
            if (!source.Exists)
            {
                throw new ImageGenerationException(
                    ImageGenerationErrorKind.NotFound,
                    "File modello sorgente non trovato.");
            }

            return new RemoteFileInfo(source.Length, null);
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Head, uri);
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new RemoteFileInfo(null, null);
            }

            return new RemoteFileInfo(
                response.Content.Headers.ContentLength,
                null);
        }
        catch (HttpRequestException)
        {
            return new RemoteFileInfo(null, null);
        }
    }

    private async Task DownloadFileCoreAsync(
        Uri uri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme == Uri.UriSchemeFile)
        {
            await using FileStream source = File.OpenRead(uri.LocalPath);
            await using FileStream destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
            return;
        }

        using HttpResponseMessage response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Unreachable,
                $"Download modello non riuscito. HTTP {(int)response.StatusCode}.");
        }

        await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private static async Task<bool> ExistingFileMatchesAsync(
        string path,
        string? expectedSha256,
        long? expectedSizeBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        FileInfo file = new(path);
        if (expectedSizeBytes is not null && file.Length != expectedSizeBytes.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            return (await ComputeSha256Async(path, cancellationToken)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
        }

        return expectedSizeBytes is not null || file.Length > 0;
    }

    private static async Task VerifyDownloadedFileAsync(
        string path,
        string? expectedSha256,
        long? expectedSizeBytes,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (expectedSizeBytes is not null && file.Length != expectedSizeBytes.Value)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Il file modello scaricato ha una dimensione diversa da quella attesa.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256)
            && !(await ComputeSha256Async(path, cancellationToken)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Il file modello scaricato non supera la verifica SHA256.");
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Uri CreateHuggingFaceResolveUri(string repositoryId, string safeRelativePath)
    {
        return new Uri(
            $"https://huggingface.co/{repositoryId}/resolve/main/{Uri.EscapeDataString(safeRelativePath).Replace("%2F", "/", StringComparison.Ordinal)}");
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt64(out long value)
            ? value
            : null;
    }

    private static string? NormalizeSha256OrNull(string value)
    {
        string normalized = value.Trim().Trim('"').ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized
            : null;
    }

    private static bool IsPlaceholderModelFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        byte[] placeholder = System.Text.Encoding.UTF8.GetBytes(ImageModelCatalog.PlaceholderModelContent);
        FileInfo file = new(path);
        if (file.Length != placeholder.Length)
        {
            return false;
        }

        byte[] content = File.ReadAllBytes(path);
        return content.AsSpan().SequenceEqual(placeholder);
    }

    private static bool HasRequiredFiles(ImageModelCatalogEntry model, string modelDirectory)
    {
        return model.RequiredFiles.Count > 0
            && model.RequiredFiles.All(requiredFile => File.Exists(ResolveModelSnapshotPath(modelDirectory, requiredFile)));
    }

    private static string? GetSnapshotVerificationError(ImageModelCatalogEntry model, string modelDirectory)
    {
        foreach (string requiredFile in model.RequiredFiles)
        {
            string requiredPath = ResolveModelSnapshotPath(modelDirectory, requiredFile);
            if (IsPlaceholderModelFile(requiredPath))
            {
                return $"Il file richiesto {requiredFile} e un segnaposto tecnico e non contiene un modello immagini eseguibile.";
            }
        }

        string modelIndexPath = ResolveModelSnapshotPath(modelDirectory, "model_index.json");
        if (!File.Exists(modelIndexPath))
        {
            return null;
        }

        try
        {
            using JsonDocument modelIndex = JsonDocument.Parse(File.ReadAllText(modelIndexPath));
            if (!modelIndex.RootElement.TryGetProperty("_class_name", out JsonElement classNameElement))
            {
                return null;
            }

            string? className = classNameElement.GetString();
            if (string.IsNullOrWhiteSpace(className))
            {
                return "model_index.json non dichiara una pipeline modello valida.";
            }

            return SupportedPipelineClasses.Any(supported =>
                className.Contains(supported, StringComparison.OrdinalIgnoreCase))
                ? null
                : $"Pipeline modello non supportata: {className}. Usa uno snapshot ONNX SDXL compatibile con DirectML.";
        }
        catch (JsonException)
        {
            return "model_index.json non e un file JSON valido.";
        }
        catch (IOException ex)
        {
            return $"model_index.json non leggibile: {ex.Message}";
        }
    }

    private static bool ModelRequiresSingleOnnxFile(ImageModelCatalogEntry model)
    {
        return model.RequiredFiles.Any(requiredFile =>
            requiredFile.Equals(ImageModelCatalog.RequiredModelFileName, StringComparison.OrdinalIgnoreCase));
    }

    private static long GetDirectorySize(string modelDirectory)
    {
        return Directory.Exists(modelDirectory)
            ? Directory.EnumerateFiles(modelDirectory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
            : 0;
    }

    private static long CalculateRemainingDownloadBytes(long expectedSizeBytes, long localSizeBytes)
    {
        return expectedSizeBytes <= 0 ? 0 : Math.Max(0, expectedSizeBytes - localSizeBytes);
    }

    private static string ResolveModelSnapshotPath(string modelDirectory, string relativePath)
    {
        string root = Path.GetFullPath(modelDirectory);
        string absolutePath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Percorso file modello non valido.");
        }

        return absolutePath;
    }

    private static bool IsHuggingFaceModelPage(Uri uri, out string repositoryId)
    {
        repositoryId = string.Empty;
        if (!uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        repositoryId = $"{segments[0]}/{segments[1]}";
        return true;
    }
}

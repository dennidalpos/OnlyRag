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
        bool isDownloading;
        lock (gate)
        {
            isDownloading = activeDownloads.Contains(model.Id);
        }

        string modelPath = GetModelFilePath(model.Id);
        if (IsPlaceholderModelFile(modelPath))
        {
            return new ImageModelLocalState(
                model.Id,
                "VerificationFailed",
                IsDownloaded: true,
                IsVerified: false,
                GetDirectorySize(modelDirectory),
                modelDirectory,
                "Il file locale e un segnaposto tecnico e non contiene un modello immagini eseguibile.",
                model.ExpectedSizeBytes,
                CalculateRemainingDownloadBytes(model.ExpectedSizeBytes, GetDirectorySize(modelDirectory)));
        }

        if (!HasRequiredFiles(model, modelDirectory))
        {
            return new ImageModelLocalState(
                model.Id,
                isDownloading ? "Downloading" : "NotDownloaded",
                IsDownloaded: false,
                IsVerified: false,
                LocalSizeBytes: 0,
                modelDirectory,
                isDownloading ? null : "Il modello non e ancora stato scaricato.",
                model.ExpectedSizeBytes,
                model.ExpectedSizeBytes);
        }

        long localSizeBytes = GetDirectorySize(modelDirectory);
        if (string.IsNullOrWhiteSpace(model.Sha256))
        {
            return new ImageModelLocalState(
                model.Id,
                "Downloaded",
                IsDownloaded: true,
                IsVerified: false,
                localSizeBytes,
                modelDirectory,
                "Modello scaricato, ma manca lo SHA256 per la verifica.",
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
                state.IsVerified
                    ? "Modello immagini scaricato e verificato."
                    : "Modello immagini scaricato. Inserisci lo SHA256 per abilitarne la verifica.");
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

    private async Task DownloadHuggingFaceSnapshotAsync(
        string repositoryId,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage metadataResponse = await httpClient.GetAsync(
            $"https://huggingface.co/api/models/{repositoryId}",
            cancellationToken);
        if (!metadataResponse.IsSuccessStatusCode)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Unreachable,
                $"Metadata modello Hugging Face non raggiungibili. HTTP {(int)metadataResponse.StatusCode}.");
        }

        using JsonDocument metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStreamAsync(cancellationToken));
        if (!metadata.RootElement.TryGetProperty("siblings", out JsonElement siblings))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Metadata modello Hugging Face senza lista file.");
        }

        foreach (JsonElement sibling in siblings.EnumerateArray())
        {
            string? relativePath = sibling.GetProperty("rfilename").GetString();
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string safeRelativePath = relativePath.Replace('\\', '/');
            string destinationPath = ResolveModelSnapshotPath(destinationDirectory, safeRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            Uri downloadUri = new($"https://huggingface.co/{repositoryId}/resolve/main/{Uri.EscapeDataString(safeRelativePath).Replace("%2F", "/", StringComparison.Ordinal)}");
            using HttpResponseMessage response = await httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ImageGenerationException(
                    ImageGenerationErrorKind.Unreachable,
                    $"Download file modello non riuscito per {safeRelativePath}. HTTP {(int)response.StatusCode}.");
            }

            await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

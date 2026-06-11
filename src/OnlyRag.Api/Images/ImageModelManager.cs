using System.Security.Cryptography;
using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal sealed class ImageModelManager
{
    private readonly InProcessBackendDescriptor descriptor;
    private readonly HttpClient httpClient;
    private readonly HashSet<string> activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();

    public ImageModelManager(InProcessBackendDescriptor descriptor, HttpClient httpClient)
    {
        this.descriptor = descriptor;
        this.httpClient = httpClient;
    }

    public IReadOnlyList<ImageModelCatalogEntry> ListCatalog()
    {
        return ImageModelCatalog.List();
    }

    public IReadOnlyList<ImageModelLocalState> ListStates()
    {
        return ImageModelCatalog.List().Select(model => GetState(model.Id)).ToArray();
    }

    public ImageModelLocalState GetState(string modelId)
    {
        ImageModelCatalogEntry model = ImageModelCatalog.Get(modelId);
        string modelDirectory = GetModelDirectory(model.Id);
        string modelPath = GetModelFilePath(model.Id);
        bool isDownloading;
        lock (gate)
        {
            isDownloading = activeDownloads.Contains(model.Id);
        }

        if (!File.Exists(modelPath))
        {
            return new ImageModelLocalState(
                model.Id,
                isDownloading ? "Downloading" : "NotDownloaded",
                IsDownloaded: false,
                IsVerified: false,
                LocalSizeBytes: 0,
                modelDirectory,
                isDownloading ? null : "Il modello non e ancora stato scaricato.");
        }

        FileInfo file = new(modelPath);
        if (IsPlaceholderModelFile(modelPath))
        {
            return new ImageModelLocalState(
                model.Id,
                "VerificationFailed",
                IsDownloaded: true,
                IsVerified: false,
                file.Length,
                modelDirectory,
                "Il file locale e un segnaposto tecnico e non contiene un modello immagini eseguibile.");
        }

        string actualSha256 = ComputeSha256(modelPath);
        bool hashMatches = actualSha256.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
        return new ImageModelLocalState(
            model.Id,
            hashMatches ? "Verified" : "VerificationFailed",
            IsDownloaded: true,
            IsVerified: hashMatches,
            file.Length,
            modelDirectory,
            hashMatches ? null : "Hash SHA256 non valido per il file modello locale.");
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

        ImageModelCatalogEntry model = ImageModelCatalog.Get(modelId);
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
            string modelPath = GetModelFilePath(model.Id);
            await DownloadModelFileAsync(model, modelPath, cancellationToken);
            ImageModelLocalState state = GetState(model.Id);
            if (!state.IsVerified)
            {
                throw new ImageGenerationException(
                    ImageGenerationErrorKind.InvalidConfiguration,
                    state.VerificationError ?? "Verifica modello immagini non riuscita.");
            }

            return new ImageModelDownloadResponse(model.Id, state.State, "Modello immagini scaricato e verificato.");
        }
        finally
        {
            lock (gate)
            {
                activeDownloads.Remove(model.Id);
            }
        }
    }

    public ImageModelDownloadResponse CancelDownload(string modelId)
    {
        ImageModelCatalogEntry model = ImageModelCatalog.Get(modelId);
        lock (gate)
        {
            if (!activeDownloads.Remove(model.Id))
            {
                return new ImageModelDownloadResponse(model.Id, GetState(model.Id).State, "Nessun download modello attivo.");
            }
        }

        return new ImageModelDownloadResponse(model.Id, "Cancelled", "Download modello annullato.");
    }

    public ImageModelDownloadResponse Delete(string modelId)
    {
        ImageModelCatalogEntry model = ImageModelCatalog.Get(modelId);
        string modelDirectory = GetModelDirectory(model.Id);
        if (Directory.Exists(modelDirectory))
        {
            Directory.Delete(modelDirectory, recursive: true);
        }

        return new ImageModelDownloadResponse(model.Id, "NotDownloaded", "File modello rimossi.");
    }

    public string GetVerifiedModelFilePath(string modelId)
    {
        ImageModelLocalState state = GetState(modelId);
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

    private string GetModelDirectory(string modelId)
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

    private async Task DownloadModelFileAsync(
        ImageModelCatalogEntry model,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Uri uri = new(model.DownloadUrl);
        if (string.Equals(uri.Scheme, "onlyrag", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidConfiguration,
                "Il modello integrato e ancora un segnaposto tecnico: la generazione immagini reale non e disponibile in questa build.");
        }

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

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsPlaceholderModelFile(string path)
    {
        byte[] placeholder = System.Text.Encoding.UTF8.GetBytes(ImageModelCatalog.PlaceholderModelContent);
        FileInfo file = new(path);
        if (file.Length != placeholder.Length)
        {
            return false;
        }

        byte[] content = File.ReadAllBytes(path);
        return content.AsSpan().SequenceEqual(placeholder);
    }
}

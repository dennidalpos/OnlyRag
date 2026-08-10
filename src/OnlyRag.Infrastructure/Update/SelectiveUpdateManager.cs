using System.Security.Cryptography;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Update;

public sealed class SelectiveUpdateManager
{
    private const string ModelIntegrityManifestFileName = "integrity-manifest.json";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly AppStoragePaths storagePaths;
    private readonly string installationRoot;

    public SelectiveUpdateManager(AppStoragePaths storagePaths, string? installationRoot = null)
    {
        this.storagePaths = storagePaths;
        this.installationRoot = Path.GetFullPath(installationRoot ?? AppContext.BaseDirectory);
    }

    public async Task<UpdateResult> ApplyAsync(
        string releaseDirectory,
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        string sourceRoot = Path.GetFullPath(releaseDirectory);
        string manifestFile = Path.GetFullPath(manifestPath);
        EnsureDirectoryExists(sourceRoot);
        EnsureFileExists(manifestFile);

        UpdateManifest manifest = await ReadManifestAsync(manifestFile, cancellationToken);
        List<string> updated = [];
        List<string> skipped = [];
        List<UpdateFailure> failed = [];

        foreach (UpdateFileEntry entry in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizeRelativePath(entry.Path);
            string sourcePath = GetContainedPath(sourceRoot, relativePath);
            string targetPath = GetContainedPath(installationRoot, relativePath);

            if (IsProtectedPath(targetPath))
            {
                failed.Add(new UpdateFailure(relativePath, "Il file appartiene ai dati locali o ai modelli e non puo essere aggiornato."));
                continue;
            }

            try
            {
                EnsureFileExists(sourcePath);
                if (new FileInfo(sourcePath).Length != entry.SizeBytes
                    || !await HasSha256Async(sourcePath, entry.Sha256, cancellationToken))
                {
                    failed.Add(new UpdateFailure(relativePath, "Il file della release non corrisponde al manifest SHA-256."));
                    continue;
                }

                if (File.Exists(targetPath)
                    && new FileInfo(targetPath).Length == entry.SizeBytes
                    && await HasSha256Async(targetPath, entry.Sha256, cancellationToken))
                {
                    skipped.Add(relativePath);
                    continue;
                }

                string? targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                string stagingPath = targetPath + ".onlyrag-update";
                await CopyFileAsync(sourcePath, stagingPath, cancellationToken);
                File.Move(stagingPath, targetPath, overwrite: true);
                updated.Add(relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(new UpdateFailure(relativePath, ex.Message));
            }
        }

        ModelIntegrityStatus modelIntegrity = await CheckModelIntegrityAsync(cancellationToken);
        return new UpdateResult(manifest.Version, updated, skipped, failed, modelIntegrity);
    }

    public async Task<ModelIntegrityStatus> CheckModelIntegrityAsync(
        CancellationToken cancellationToken = default)
    {
        string manifestPath = Path.Combine(storagePaths.DataRoot, ModelIntegrityManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return ModelIntegrityStatus.Healthy();
        }

        ModelIntegrityManifest manifest = await ReadModelManifestAsync(manifestPath, cancellationToken);
        List<ModelIntegrityIssue> issues = [];
        foreach (ModelIntegrityEntry entry in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizeRelativePath(entry.Path);
            string modelPath = GetContainedPath(storagePaths.DataRoot, relativePath);
            if (!File.Exists(modelPath))
            {
                issues.Add(new ModelIntegrityIssue(relativePath, "File modello mancante."));
                continue;
            }

            if (entry.SizeBytes.HasValue && new FileInfo(modelPath).Length != entry.SizeBytes.Value)
            {
                issues.Add(new ModelIntegrityIssue(relativePath, "Dimensione file modello inattesa."));
                continue;
            }

            if (!await HasSha256Async(modelPath, entry.Sha256, cancellationToken))
            {
                issues.Add(new ModelIntegrityIssue(relativePath, "Hash SHA-256 del modello non valido."));
            }
        }

        return new ModelIntegrityStatus(issues.Count == 0, issues, issues.Count > 0);
    }

    private bool IsProtectedPath(string path)
    {
        return IsPathUnder(path, storagePaths.DataRoot)
            || IsPathUnder(path, storagePaths.DocumentsRoot)
            || IsPathUnder(path, storagePaths.ImageModelsDirectory)
            || IsPathUnder(path, storagePaths.RerankerModelsDirectory);
    }

    private static async Task<UpdateManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<UpdateManifest>(
            stream,
            ManifestJsonOptions,
            cancellationToken)
            ?? throw new InvalidDataException("Manifest di aggiornamento vuoto o non valido.");
    }

    private static async Task<ModelIntegrityManifest> ReadModelManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ModelIntegrityManifest>(
            stream,
            ManifestJsonOptions,
            cancellationToken)
            ?? throw new InvalidDataException("Manifest di integrita modelli vuoto o non valido.");
    }

    private static async Task<bool> HasSha256Async(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] actual = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(actual).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using FileStream source = File.OpenRead(sourcePath);
        await using FileStream destination = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"Percorso manifest non valido: '{path}'.");
        }

        string normalized = path.Replace('\\', '/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            throw new InvalidDataException($"Percorso manifest non valido: '{path}'.");
        }

        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string GetContainedPath(string root, string relativePath)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!IsPathUnder(fullPath, fullRoot))
        {
            throw new InvalidDataException($"Percorso fuori dalla radice consentita: '{relativePath}'.");
        }

        return fullPath;
    }

    private static bool IsPathUnder(string path, string root)
    {
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory release non trovata: '{path}'.");
        }
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File richiesto non trovato: '{path}'.", path);
        }
    }

    private sealed record ModelIntegrityManifest(IReadOnlyList<ModelIntegrityEntry> Files);

    private sealed record ModelIntegrityEntry(string Path, string Sha256, long? SizeBytes = null);
}

using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Api;

internal sealed class WorkspaceService
{
    private readonly string settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public WorkspaceService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(localAppData, "OnlyRag");
        Directory.CreateDirectory(folder);
        settingsFilePath = Path.Combine(folder, "workspace_settings.json");
    }

    public async Task<WorkspaceConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        PersistedWorkspaceData data = await LoadDataAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(data.RootPath) || !Directory.Exists(data.RootPath))
        {
            return new WorkspaceConfig(
                RootPath: null,
                IsAuthorized: false,
                CanRead: false,
                CanWrite: false,
                FileCount: 0,
                LastVerifiedAt: null);
        }

        bool canRead = CheckReadPermission(data.RootPath);
        bool canWrite = CheckWritePermission(data.RootPath);
        int count = countFiles(data.RootPath);

        return new WorkspaceConfig(
            RootPath: data.RootPath,
            IsAuthorized: canRead,
            CanRead: canRead,
            CanWrite: canWrite,
            FileCount: count,
            LastVerifiedAt: DateTimeOffset.UtcNow);
    }

    public async Task<WorkspaceConfig> SelectWorkspaceAsync(SelectWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath))
        {
            throw new ArgumentException("Il percorso della cartella di progetto non puo essere vuoto.", nameof(request));
        }

        string fullPath = Path.GetFullPath(request.FolderPath.Trim());
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"La cartella specificata non esiste sul sistema: {fullPath}");
        }

        bool canRead = CheckReadPermission(fullPath);
        bool canWrite = CheckWritePermission(fullPath);

        if (!canRead)
        {
            throw new UnauthorizedAccessException($"Impossibile accedere in lettura alla cartella {fullPath}.");
        }

        PersistedWorkspaceData data = new(fullPath, DateTimeOffset.UtcNow);
        await SaveDataAsync(data, cancellationToken);

        int count = countFiles(fullPath);

        return new WorkspaceConfig(
            RootPath: fullPath,
            IsAuthorized: true,
            CanRead: canRead,
            CanWrite: canWrite,
            FileCount: count,
            LastVerifiedAt: DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<WorkspaceFileItem>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        WorkspaceConfig config = await GetConfigAsync(cancellationToken);
        if (!config.IsAuthorized || string.IsNullOrWhiteSpace(config.RootPath))
        {
            return Array.Empty<WorkspaceFileItem>();
        }

        DirectoryInfo root = new(config.RootPath);
        List<WorkspaceFileItem> items = new();

        var entries = root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Ignore common noise directories
            if (entry.FullName.Contains("\\.git\\") ||
                entry.FullName.Contains("\\node_modules\\") ||
                entry.FullName.Contains("\\bin\\") ||
                entry.FullName.Contains("\\obj\\") ||
                entry.FullName.Contains("\\.vs\\"))
            {
                continue;
            }

            string relative = Path.GetRelativePath(config.RootPath, entry.FullName);
            bool isDir = (entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
            long size = isDir ? 0 : ((FileInfo)entry).Length;

            items.Add(new WorkspaceFileItem(
                RelativePath: relative.Replace('\\', '/'),
                FullPath: entry.FullName,
                IsDirectory: isDir,
                SizeBytes: size,
                LastModified: entry.LastWriteTimeUtc));

            if (items.Count >= 500) break; // Limit to 500 items for performance
        }

        return items.OrderBy(i => i.IsDirectory ? 0 : 1).ThenBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<ReadWorkspaceFileResponse> ReadFileAsync(ReadWorkspaceFileRequest request, CancellationToken cancellationToken = default)
    {
        string safePath = await ResolveSafePathAsync(request.RelativePath, cancellationToken);
        if (!File.Exists(safePath))
        {
            throw new FileNotFoundException($"File non trovato nel workspace autorizzato: {request.RelativePath}");
        }

        FileInfo fi = new(safePath);
        if (fi.Length > 2_000_000)
        {
            throw new InvalidOperationException($"Il file {request.RelativePath} supera il limite massimo di 2 MB per la lettura nel contesto di coding.");
        }

        string content = await File.ReadAllTextAsync(safePath, cancellationToken);
        string language = InferLanguage(fi.Extension);

        return new ReadWorkspaceFileResponse(
            RelativePath: request.RelativePath,
            Content: content,
            SizeBytes: fi.Length,
            Language: language);
    }

    public async Task<WriteWorkspaceFileResponse> WriteFileAsync(WriteWorkspaceFileRequest request, CancellationToken cancellationToken = default)
    {
        string safePath = await ResolveSafePathAsync(request.RelativePath, cancellationToken);
        string? parent = Path.GetDirectoryName(safePath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllTextAsync(safePath, request.Content ?? string.Empty, cancellationToken);

        return new WriteWorkspaceFileResponse(
            RelativePath: request.RelativePath,
            Success: true,
            Message: $"File salvato con successo nel workspace autorizzato ({request.RelativePath}).");
    }

    public async Task<WorkspaceConfig?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        string psScript = "[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = 'Seleziona cartella di progetto per OnlyRag'; $f.ShowNewFolderButton = $true; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { Write-Output $f.SelectedPath }";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{psScript}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null) return null;

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        string selectedPath = output.Trim();
        if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
        {
            return null;
        }

        return await SelectWorkspaceAsync(new SelectWorkspaceRequest(selectedPath), cancellationToken);
    }

    public async Task<bool> OpenExternalFileAsync(string relativeOrFullPath, CancellationToken cancellationToken = default)
    {
        WorkspaceConfig config = await GetConfigAsync(cancellationToken);
        string targetPath = relativeOrFullPath;

        if (!Path.IsPathRooted(targetPath))
        {
            if (string.IsNullOrWhiteSpace(config.RootPath)) return false;
            targetPath = Path.Combine(config.RootPath, relativeOrFullPath);
        }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return false;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = targetPath,
            UseShellExecute = true
        });

        return true;
    }


    private async Task<string> ResolveSafePathAsync(string relativePath, CancellationToken cancellationToken)
    {
        WorkspaceConfig config = await GetConfigAsync(cancellationToken);
        if (!config.IsAuthorized || string.IsNullOrWhiteSpace(config.RootPath))
        {
            throw new InvalidOperationException("Nessun workspace di progetto e stato autorizzato sul sistema.");
        }

        string root = Path.GetFullPath(config.RootPath);
        string target = Path.GetFullPath(Path.Combine(root, relativePath.TrimStart('/', '\\')));

        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Tentativo di accesso esterno alla cartella di progetto autorizzata (Path Traversal bloccato).");
        }

        return target;
    }

    private async Task<PersistedWorkspaceData> LoadDataAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsFilePath))
        {
            return new PersistedWorkspaceData(null, null);
        }

        try
        {
            string json = await File.ReadAllTextAsync(settingsFilePath, cancellationToken);
            return JsonSerializer.Deserialize<PersistedWorkspaceData>(json, JsonOptions) ?? new PersistedWorkspaceData(null, null);
        }
        catch
        {
            return new PersistedWorkspaceData(null, null);
        }
    }

    private async Task SaveDataAsync(PersistedWorkspaceData data, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(settingsFilePath, json, cancellationToken);
    }

    private static bool CheckReadPermission(string path)
    {
        try
        {
            _ = Directory.GetFileSystemEntries(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckWritePermission(string path)
    {
        try
        {
            string testFile = Path.Combine(path, $".onlyrag_write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int countFiles(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string InferLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" => "javascript",
            ".py" => "python",
            ".sql" => "sql",
            ".ps1" or ".psm1" => "powershell",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".json" => "json",
            ".md" => "markdown",
            _ => "text"
        };
    }

    private sealed record PersistedWorkspaceData(string? RootPath, DateTimeOffset? SavedAt);
}

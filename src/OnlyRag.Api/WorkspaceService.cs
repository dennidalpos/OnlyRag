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

    public async Task<WorkspaceConfig> ClearWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        PersistedWorkspaceData emptyData = new(null, null);
        await SaveDataAsync(emptyData, cancellationToken);

        return new WorkspaceConfig(
            RootPath: null,
            IsAuthorized: false,
            CanRead: false,
            CanWrite: false,
            FileCount: 0,
            LastVerifiedAt: null);
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
        if (!config.IsAuthorized || string.IsNullOrWhiteSpace(config.RootPath))
        {
            return false;
        }

        string targetPath = await ResolveSafePathAsync(
            Path.IsPathRooted(relativeOrFullPath)
                ? Path.GetRelativePath(config.RootPath, relativeOrFullPath)
                : relativeOrFullPath,
            cancellationToken);

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

    public async Task<DeleteWorkspaceFileResponse> DeleteFileAsync(DeleteWorkspaceFileRequest request, CancellationToken cancellationToken = default)
    {
        string safePath = await ResolveSafePathAsync(request.RelativePath, cancellationToken);
        if (File.Exists(safePath))
        {
            File.Delete(safePath);
            return new DeleteWorkspaceFileResponse(
                RelativePath: request.RelativePath,
                Success: true,
                Message: $"File eliminato dal workspace ({request.RelativePath}).");
        }
        else if (Directory.Exists(safePath))
        {
            Directory.Delete(safePath, true);
            return new DeleteWorkspaceFileResponse(
                RelativePath: request.RelativePath,
                Success: true,
                Message: $"Cartella eliminata dal workspace ({request.RelativePath}).");
        }

        return new DeleteWorkspaceFileResponse(
            RelativePath: request.RelativePath,
            Success: false,
            Message: $"Elemento non trovato nel workspace ({request.RelativePath}).");
    }

    public async Task<ExecuteWorkspaceCommandResponse> ExecuteCommandAsync(ExecuteWorkspaceCommandRequest request, CancellationToken cancellationToken = default)
    {
        WorkspaceConfig config = await GetConfigAsync(cancellationToken);
        if (!config.IsAuthorized || string.IsNullOrWhiteSpace(config.RootPath) || !Directory.Exists(config.RootPath))
        {
            throw new InvalidOperationException("Nessun workspace di progetto autorizzato sul sistema per l'esecuzione di comandi.");
        }

        string commandLine = string.IsNullOrWhiteSpace(request.Arguments)
            ? request.Command?.Trim() ?? string.Empty
            : $"{request.Command?.Trim()} {request.Arguments.Trim()}";
        string[] commandParts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (commandParts.Length == 0 || commandParts.Any(ContainsShellMetacharacters))
        {
            throw new UnauthorizedAccessException("Comando non valido: sono consentiti solo eseguibili e argomenti senza shell.");
        }

        string executable = commandParts[0];
        string executableName = Path.GetFileName(executable);
        string[] allowedExecutables = ["dotnet", "npm", "node", "git"];
        if (Path.IsPathRooted(executable)
            || !allowedExecutables.Contains(executableName, StringComparer.OrdinalIgnoreCase)
            || commandParts.Skip(1).Any(IsForbiddenCommandArgument))
        {
            throw new UnauthorizedAccessException("Eseguibile non autorizzato dal workspace sandbox.");
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = config.RootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in commandParts.Skip(1))
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(cancellationToken);

        string output = await outputTask;
        string error = await errorTask;

        return new ExecuteWorkspaceCommandResponse(
            Success: process.ExitCode == 0,
            ExitCode: process.ExitCode,
            Output: output,
            Error: error);
    }



    private async Task<string> ResolveSafePathAsync(string relativePath, CancellationToken cancellationToken)
    {
        WorkspaceConfig config = await GetConfigAsync(cancellationToken);
        if (!config.IsAuthorized || string.IsNullOrWhiteSpace(config.RootPath))
        {
            throw new InvalidOperationException("Nessun workspace di progetto e stato autorizzato sul sistema.");
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.RootPath));
        string target = Path.GetFullPath(Path.Combine(root, relativePath.TrimStart('/', '\\')));

        if (!IsPathWithinRoot(root, target) || ContainsReparsePointOutsideRoot(root, target))
        {
            throw new UnauthorizedAccessException("Tentativo di accesso esterno alla cartella di progetto autorizzata (Path Traversal bloccato).");
        }

        return target;
    }

    private static bool IsPathWithinRoot(string root, string candidate)
    {
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return normalizedCandidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePointOutsideRoot(string root, string target)
    {
        string? current = File.Exists(target) || Directory.Exists(target)
            ? target
            : Path.GetDirectoryName(target);

        while (!string.IsNullOrWhiteSpace(current) && IsPathWithinRoot(root, current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return !Path.GetFullPath(current).Equals(root, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (Path.GetFullPath(current).Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private static bool ContainsShellMetacharacters(string value) =>
        value.IndexOfAny([';', '&', '|', '>', '<', '`', '"', '\'']) >= 0;

    private static bool IsForbiddenCommandArgument(string value) =>
        value.Equals("-c", StringComparison.OrdinalIgnoreCase)
        || value.Equals("--eval", StringComparison.OrdinalIgnoreCase)
        || value.Equals("-e", StringComparison.OrdinalIgnoreCase)
        || value.Equals("exec", StringComparison.OrdinalIgnoreCase);

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

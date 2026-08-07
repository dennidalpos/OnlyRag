using System.Text.Json;

namespace OnlyRag.Api;

/// <summary>
/// Owns the on-disk lifecycle of the private OCR virtual environment. A new runtime is
/// assembled outside the live location and published only after the bridge has verified it.
/// </summary>
internal sealed class OcrRuntimeEnvironment
{
    private const string VenvDirectoryName = ".venv";
    private const string StampFileName = ".requirements-stamp";
    private readonly string installRoot;

    public OcrRuntimeEnvironment(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        this.installRoot = Path.GetFullPath(installRoot);
    }

    public OcrRuntimeEnvironmentDiagnostic Inspect()
    {
        string livePath = LivePath;
        if (!Directory.Exists(livePath))
        {
            return new OcrRuntimeEnvironmentDiagnostic("missing", "OCR environment has not been created yet.");
        }

        if (!File.Exists(PythonPath(livePath)))
        {
            return new OcrRuntimeEnvironmentDiagnostic("corrupt", "The OCR environment is missing its Python executable.");
        }

        string stampPath = Path.Combine(livePath, StampFileName);
        if (!File.Exists(stampPath))
        {
            return new OcrRuntimeEnvironmentDiagnostic("incomplete", "The OCR environment has no verified runtime stamp.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(stampPath));
            bool hasRuntime = document.RootElement.TryGetProperty("runtimeName", out JsonElement runtime)
                && runtime.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(runtime.GetString());
            bool hasRequirements = document.RootElement.TryGetProperty("requirementsFile", out JsonElement requirements)
                && requirements.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(requirements.GetString());
            return hasRuntime && hasRequirements
                ? new OcrRuntimeEnvironmentDiagnostic("ready", "The OCR environment has a verified runtime stamp.")
                : new OcrRuntimeEnvironmentDiagnostic("incomplete", "The OCR environment runtime stamp is invalid.");
        }
        catch (JsonException)
        {
            return new OcrRuntimeEnvironmentDiagnostic("corrupt", "The OCR environment runtime stamp cannot be read.");
        }
    }

    public void EnsureStampIfMissing(string runtimeName, string requirementsFile)
    {
        string livePath = LivePath;
        if (!Directory.Exists(livePath) || !File.Exists(PythonPath(livePath)))
        {
            return;
        }

        string stampPath = Path.Combine(livePath, StampFileName);
        if (!File.Exists(stampPath))
        {
            try
            {
                File.WriteAllText(
                    stampPath,
                    JsonSerializer.Serialize(new { runtimeName, requirementsFile, updatedAt = DateTimeOffset.UtcNow }));
            }
            catch
            {
            }
        }
    }

    public OcrRuntimeEnvironmentTransaction BeginTransaction()
    {
        Directory.CreateDirectory(installRoot);
        string stagingPath = Path.Combine(installRoot, $".venv.staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingPath);
        return new OcrRuntimeEnvironmentTransaction(this, stagingPath);
    }

    private string LivePath => Path.Combine(installRoot, VenvDirectoryName);

    private static string PythonPath(string venvPath) => Path.Combine(venvPath, "Scripts", "python.exe");

    internal void Commit(string stagingPath, string runtimeName, string requirementsFile)
    {
        if (!File.Exists(PythonPath(stagingPath)))
        {
            throw new InvalidOperationException("OCR runtime verification completed without creating the staging Python executable.");
        }

        File.WriteAllText(
            Path.Combine(stagingPath, StampFileName),
            JsonSerializer.Serialize(new { runtimeName, requirementsFile, updatedAt = DateTimeOffset.UtcNow }));

        string? backupPath = null;
        try
        {
            if (Directory.Exists(LivePath))
            {
                backupPath = Path.Combine(installRoot, $".venv.previous-{Guid.NewGuid():N}");
                Directory.Move(LivePath, backupPath);
            }

            Directory.Move(stagingPath, LivePath);
        }
        catch
        {
            if (backupPath is not null && !Directory.Exists(LivePath) && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, LivePath);
            }

            throw;
        }

        if (backupPath is not null && Directory.Exists(backupPath))
        {
            Directory.Delete(backupPath, recursive: true);
        }
    }

    internal static void DeleteStaging(string stagingPath)
    {
        if (Directory.Exists(stagingPath))
        {
            Directory.Delete(stagingPath, recursive: true);
        }
    }
}

internal sealed record OcrRuntimeEnvironmentDiagnostic(string State, string Detail);

internal sealed class OcrRuntimeEnvironmentTransaction : IDisposable
{
    private readonly OcrRuntimeEnvironment environment;
    private bool completed;

    internal OcrRuntimeEnvironmentTransaction(OcrRuntimeEnvironment environment, string stagingPath)
    {
        this.environment = environment;
        StagingPath = stagingPath;
    }

    public string StagingPath { get; }

    public string PythonPath => Path.Combine(StagingPath, "Scripts", "python.exe");

    public void Commit(string runtimeName, string requirementsFile)
    {
        environment.Commit(StagingPath, runtimeName, requirementsFile);
        completed = true;
    }

    public void Dispose()
    {
        if (!completed)
        {
            OcrRuntimeEnvironment.DeleteStaging(StagingPath);
        }
    }
}

using System.Diagnostics;
using OnlyRag.Core;

namespace OnlyRag.Api;

internal static class BackendLog
{
    private const long MaxLogFileSizeBytes = 5 * 1024 * 1024;
    private const int MaxLogFiles = 3;
    private const string LogFileName = "backend.log";

    public static void Write(AppStoragePaths paths, string message) =>
        WriteCore(paths, correlationId: null, message);

    public static void Write(AppStoragePaths paths, string? correlationId, string message) =>
        WriteCore(paths, correlationId, message);

    public static void WriteException(AppStoragePaths paths, string? correlationId, string context, Exception exception)
    {
        string exInfo = $"{exception.GetType().Name}: {SanitizeLogMessage(exception.Message)}";
        if (exception.InnerException is not null)
        {
            exInfo += $" [{exception.InnerException.GetType().Name}: {SanitizeLogMessage(exception.InnerException.Message)}]";
        }

        WriteCore(paths, correlationId, $"{SanitizeLogMessage(context)} {exInfo}");
    }

    public static string ResolveAppVersion()
    {
        Version? version = typeof(BackendLog).Assembly.GetName().Version;
        return version is not null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "1.0.0";
    }

    private static void WriteCore(AppStoragePaths paths, string? correlationId, string message)
    {
        string prefix = correlationId is not null ? $"[{correlationId}] " : string.Empty;
        string line = $"{DateTimeOffset.Now:O} {prefix}{SanitizeLogMessage(message)}{Environment.NewLine}";
        Debug.Write(line);

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            string logPath = Path.Combine(paths.LogsDirectory, LogFileName);
            RotateIfNeeded(logPath);
            File.AppendAllText(logPath, line);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OnlyRag backend log write failed: {SanitizeLogMessage(ex.Message)}");
        }
    }

    private static string SanitizeLogMessage(string? message)
    {
        return UserFacingErrorText.FromExternalDetail(message, "Messaggio log non disponibile.");
    }

    private static void RotateIfNeeded(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return;
        }

        if (new FileInfo(logPath).Length < MaxLogFileSizeBytes)
        {
            return;
        }

        for (int i = MaxLogFiles - 1; i >= 1; i--)
        {
            string older = $"{logPath}.{i}";
            string newer = i == 1 ? logPath : $"{logPath}.{i - 1}";
            if (File.Exists(older))
            {
                File.Delete(older);
            }

            if (File.Exists(newer))
            {
                File.Move(newer, older);
            }
        }
    }
}

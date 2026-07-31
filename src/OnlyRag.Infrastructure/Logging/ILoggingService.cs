using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Logging;

public interface ILoggingService
{
    event Action<LogEntry>? OnLogWritten;

    Task<LoggingSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task UpdateSettingsAsync(LoggingSettings settings, CancellationToken cancellationToken = default);

    void Log(AppLogLevel level, string category, string message, Exception? exception = null, object? data = null);
    void LogTrace(string category, string message, object? data = null);
    void LogDebug(string category, string message, object? data = null);
    void LogInfo(string category, string message, object? data = null);
    void LogWarning(string category, string message, Exception? exception = null);
    void LogError(string category, string message, Exception? exception = null);

    IReadOnlyList<LogEntry> GetRecentLogs(AppLogLevel? minLevel = null, string? search = null, int limit = 200);
    LogStorageInfo GetStorageInfo();
    Task ClearLogsAsync(CancellationToken cancellationToken = default);
}

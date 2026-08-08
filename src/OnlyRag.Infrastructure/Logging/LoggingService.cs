using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Logging;

public sealed class LoggingService : ILoggingService, IAsyncDisposable
{
    private const int MaxMemoryLogs = 1000;
    private const int MaxPendingWrites = 2000;
    private const int WriteBatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    private readonly AppStoragePaths storagePaths;
    private readonly LoggingSettingsStore settingsStore;
    private readonly ConcurrentQueue<LogEntry> memoryLogs = new();
    private readonly Channel<LogEntry> fileWriteChannel;
    private readonly Task backgroundWriterTask;
    private readonly CancellationTokenSource writerCts = new();
    private AppLogLevel currentMinLevel = AppLogLevel.Trace;

    public event Action<LogEntry>? OnLogWritten;

    public LoggingService(AppStoragePaths storagePaths, LoggingSettingsStore settingsStore)
    {
        this.storagePaths = storagePaths;
        this.settingsStore = settingsStore;

        Directory.CreateDirectory(storagePaths.LogsDirectory);

        fileWriteChannel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(MaxPendingWrites)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        backgroundWriterTask = Task.Run(() => BackgroundWriterLoopAsync(writerCts.Token));

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogError("UnhandledException", "Unhandled AppDomain exception occurred.", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError("TaskScheduler", "Unobserved task exception occurred.", args.Exception);
            args.SetObserved();
        };

        // Caricamento iniziale asincrono in background per non bloccare l'inizializzazione
        Task.Run(async () =>
        {
            try
            {
                var settings = await settingsStore.GetAsync();
                currentMinLevel = settings.MinLevel;
            }
            catch
            {
                currentMinLevel = AppLogLevel.Trace;
            }
        });
    }

    public async Task<LoggingSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        currentMinLevel = settings.MinLevel;
        return settings;
    }

    public async Task UpdateSettingsAsync(LoggingSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await settingsStore.SaveAsync(settings, cancellationToken);
        currentMinLevel = settings.MinLevel;
        LogInfo("System", $"Livello di verbosità log aggiornato a: {settings.MinLevel}");
    }

    public void Log(AppLogLevel level, string category, string message, Exception? exception = null, object? data = null)
    {
        if (currentMinLevel == AppLogLevel.None || level < currentMinLevel)
        {
            return;
        }

        string? exceptionDetails = exception?.ToString();
        string? dataJson = null;
        if (data != null)
        {
            try
            {
                dataJson = JsonSerializer.Serialize(data);
            }
            catch
            {
                dataJson = data.ToString();
            }
        }

        var entry = new LogEntry(
            Id: $"log_{Guid.NewGuid():N}"[..12],
            TimestampUtc: DateTime.UtcNow,
            Level: level,
            Category: category ?? "General",
            Message: message ?? string.Empty,
            ExceptionDetails: exceptionDetails,
            DataJson: dataJson);

        memoryLogs.Enqueue(entry);
        while (memoryLogs.Count > MaxMemoryLogs)
        {
            memoryLogs.TryDequeue(out _);
        }

        // Non-blocking write to channel — drops oldest if full
        fileWriteChannel.Writer.TryWrite(entry);

        try
        {
            OnLogWritten?.Invoke(entry);
        }
        catch
        {
            // Ignora errori nei subscriber
        }
    }

    public void LogTrace(string category, string message, object? data = null) => Log(AppLogLevel.Trace, category, message, null, data);
    public void LogDebug(string category, string message, object? data = null) => Log(AppLogLevel.Debug, category, message, null, data);
    public void LogInfo(string category, string message, object? data = null) => Log(AppLogLevel.Information, category, message, null, data);
    public void LogWarning(string category, string message, Exception? exception = null) => Log(AppLogLevel.Warning, category, message, exception, null);
    public void LogError(string category, string message, Exception? exception = null) => Log(AppLogLevel.Error, category, message, exception, null);

    public IReadOnlyList<LogEntry> GetRecentLogs(AppLogLevel? minLevel = null, string? search = null, int limit = 200)
    {
        var logs = memoryLogs.ToArray();
        IEnumerable<LogEntry> query = logs;

        if (minLevel.HasValue)
        {
            query = query.Where(l => l.Level >= minLevel.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(l =>
                l.Message.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                l.Category.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (l.ExceptionDetails != null && l.ExceptionDetails.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderByDescending(l => l.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToList();
    }

    public LogStorageInfo GetStorageInfo()
    {
        long totalBytes = 0;
        int fileCount = 0;

        if (Directory.Exists(storagePaths.LogsDirectory))
        {
            var files = Directory.GetFiles(storagePaths.LogsDirectory, "*.*", SearchOption.TopDirectoryOnly);
            fileCount = files.Length;
            foreach (var file in files)
            {
                try
                {
                    totalBytes += new FileInfo(file).Length;
                }
                catch
                {
                    // Ignora file temporaneamente bloccati
                }
            }
        }

        return new LogStorageInfo(
            TotalSizeBytes: totalBytes,
            FormattedSize: FormatBytes(totalBytes),
            MemoryEntryCount: memoryLogs.Count,
            FileCount: fileCount,
            LogDirectory: storagePaths.LogsDirectory);
    }

    public Task ClearLogsAsync(CancellationToken cancellationToken = default)
    {
        while (memoryLogs.TryDequeue(out _)) { }

        if (Directory.Exists(storagePaths.LogsDirectory))
        {
            var files = Directory.GetFiles(storagePaths.LogsDirectory, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // In caso di file aperto in scrittura, prova a svuotarlo
                    try { File.WriteAllText(file, string.Empty); } catch { }
                }
            }
        }

        return Task.CompletedTask;
    }

    private bool isDisposed;

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }
        isDisposed = true;

        fileWriteChannel.Writer.TryComplete();
        try
        {
            await writerCts.CancelAsync();
            await backgroundWriterTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Ensure shutdown is not blocked
        }
        finally
        {
            writerCts.Dispose();
        }
    }

    private async Task BackgroundWriterLoopAsync(CancellationToken cancellationToken)
    {
        var batch = new List<LogEntry>(WriteBatchSize);
        var reader = fileWriteChannel.Reader;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                batch.Clear();

                // Wait for the first entry (blocks until data or channel completes)
                if (!await reader.WaitToReadAsync(cancellationToken))
                {
                    break;
                }

                // Drain available entries up to batch size
                while (batch.Count < WriteBatchSize && reader.TryRead(out LogEntry? entry))
                {
                    batch.Add(entry);
                }

                // If we have fewer than a full batch, wait briefly for more to arrive
                if (batch.Count < WriteBatchSize)
                {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    delayCts.CancelAfter(FlushInterval);
                    try
                    {
                        while (batch.Count < WriteBatchSize && await reader.WaitToReadAsync(delayCts.Token))
                        {
                            while (batch.Count < WriteBatchSize && reader.TryRead(out LogEntry? extra))
                            {
                                batch.Add(extra);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Flush timer expired — write what we have
                    }
                }

                FlushBatch(batch);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown requested — fall through to drain
        }

        // Drain any remaining entries on shutdown
        batch.Clear();
        while (reader.TryRead(out LogEntry? remaining))
        {
            batch.Add(remaining);
        }
        if (batch.Count > 0)
        {
            FlushBatch(batch);
        }
    }

    private void FlushBatch(List<LogEntry> batch)
    {
        if (batch.Count == 0) return;

        try
        {
            // Group by date to handle midnight rollovers
            foreach (var group in batch.GroupBy(e => e.TimestampUtc.Date))
            {
                string logFilePath = Path.Combine(storagePaths.LogsDirectory, $"onlyrag-{group.Key:yyyy-MM-dd}.log");
                var sb = new StringBuilder(batch.Count * 200);

                foreach (LogEntry entry in group)
                {
                    sb.Append($"[{entry.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff Z}] [{entry.Level.ToString().ToUpperInvariant()}] [{entry.Category}] {entry.Message}");
                    if (!string.IsNullOrEmpty(entry.DataJson))
                    {
                        sb.Append($" | DATA: {entry.DataJson}");
                    }
                    if (!string.IsNullOrEmpty(entry.ExceptionDetails))
                    {
                        sb.AppendLine();
                        sb.Append($"[EXCEPTION] {entry.ExceptionDetails}");
                    }
                    sb.AppendLine();
                }

                File.AppendAllText(logFilePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Ignora fallimenti scrittura file log per evitare eccezioni a catena
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        int digitGroups = (int)(Math.Log10(bytes) / Math.Log10(1024));
        digitGroups = Math.Min(digitGroups, units.Length - 1);
        return $"{bytes / Math.Pow(1024, digitGroups):F2} {units[digitGroups]}";
    }
}

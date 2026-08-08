using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace OnlyRag.Core.Logging;

/// <summary>
/// Early-stage logger for the primary bootstrap phase.
/// Initialized immediately at executable boot before WPF UI or DI container.
/// Flushes immediately to disk so startup logs survive crashes.
/// </summary>
public static class EarlyBootstrapperLogger
{
    private static readonly Stopwatch WallClock = Stopwatch.StartNew();
    private static readonly object SyncLock = new();
    private static StreamWriter? logWriter;
    private static int mainThreadId;
    private static bool isInitialized;

    public static void Initialize(string logDirectoryPath)
    {
        if (isInitialized) return;

        lock (SyncLock)
        {
            if (isInitialized) return;

            mainThreadId = Environment.CurrentManagedThreadId;
            try
            {
                Directory.CreateDirectory(logDirectoryPath);
                string logFilePath = Path.Combine(logDirectoryPath, "startup-bootstrap.log");

                FileStream fileStream = new(
                    logFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);

                logWriter = new StreamWriter(fileStream) { AutoFlush = true };
                isInitialized = true;

                WriteEntry(new BootstrapLogEntry(
                    TimestampUtc: DateTimeOffset.UtcNow,
                    ElapsedMs: WallClock.ElapsedMilliseconds,
                    ThreadId: mainThreadId,
                    IsMainUIThread: true,
                    Phase: "BOOTSTRAP_START",
                    Status: "SUCCESS",
                    DurationMs: 0,
                    Detail: "EarlyBootstrapperLogger initialized at absolute app entrypoint."
                ));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EarlyBootstrapperLogger] Failed to initialize startup log: {ex.Message}");
            }
        }
    }

    public static void Close()
    {
        lock (SyncLock)
        {
            if (!isInitialized) return;

            try
            {
                logWriter?.Flush();
                logWriter?.Dispose();
            }
            catch
            {
            }
            finally
            {
                logWriter = null;
                isInitialized = false;
            }
        }
    }

    public static BootstrapperScope TraceScope(string phaseName, string? detail = null)
    {
        return new BootstrapperScope(phaseName, detail);
    }

    public static void LogMilestone(string phaseName, string detail, string status = "SUCCESS")
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        bool isUi = currentThreadId == mainThreadId;

        WriteEntry(new BootstrapLogEntry(
            TimestampUtc: DateTimeOffset.UtcNow,
            ElapsedMs: WallClock.ElapsedMilliseconds,
            ThreadId: currentThreadId,
            IsMainUIThread: isUi,
            Phase: phaseName,
            Status: status,
            DurationMs: 0,
            Detail: detail
        ));
    }

    internal static void LogScopeEnd(string phaseName, long startTimestamp, Exception? exception, string? detail)
    {
        double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        long durationMs = (long)Math.Round(elapsedMs);
        int currentThreadId = Environment.CurrentManagedThreadId;
        bool isUi = currentThreadId == mainThreadId;

        string status = exception is null ? "SUCCESS" : "ERROR";
        string fullDetail = exception is null
            ? (detail ?? string.Empty)
            : $"[EXCEPTION: {exception.GetType().Name}] {exception.Message} | Detail: {detail}";

        WriteEntry(new BootstrapLogEntry(
            TimestampUtc: DateTimeOffset.UtcNow,
            ElapsedMs: WallClock.ElapsedMilliseconds,
            ThreadId: currentThreadId,
            IsMainUIThread: isUi,
            Phase: phaseName,
            Status: status,
            DurationMs: durationMs,
            Detail: fullDetail
        ));
    }

    private static void WriteEntry(BootstrapLogEntry entry)
    {
        string formattedLine = $"[{entry.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff zzz}] [{entry.Status}] [Thread {entry.ThreadId}{(entry.IsMainUIThread ? " (UI)" : " (Worker)")}] [{entry.Phase}] ({entry.DurationMs}ms) - {entry.Detail}";

        lock (SyncLock)
        {
            try
            {
                logWriter?.WriteLine(formattedLine);
            }
            catch
            {
                Debug.WriteLine(formattedLine);
            }
        }
    }
}

public readonly struct BootstrapperScope : IDisposable
{
    private readonly string phaseName;
    private readonly string? detail;
    private readonly long startTimestamp;

    public BootstrapperScope(string phaseName, string? detail)
    {
        this.phaseName = phaseName;
        this.detail = detail;
        this.startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        EarlyBootstrapperLogger.LogScopeEnd(phaseName, startTimestamp, null, detail);
    }
}

public record BootstrapLogEntry(
    DateTimeOffset TimestampUtc,
    long ElapsedMs,
    int ThreadId,
    bool IsMainUIThread,
    string Phase,
    string Status,
    long DurationMs,
    string Detail
);

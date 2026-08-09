using Microsoft.Extensions.Logging;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed class LoggingServiceTests
{
    [Fact]
    public void LoggerProvider_ForwardsStandardInformationLogsWithoutFilteringFrameworkCategories()
    {
        var storagePaths = CreateStoragePaths();
        var loggingService = CreateLoggingService(storagePaths);
        var provider = new OnlyRagLoggerProvider(loggingService);
        ILogger logger = provider.CreateLogger("Microsoft.AspNetCore.Hosting.Internal.HostingApplication");

        logger.LogInformation("Application startup completed");

        var entries = loggingService.GetRecentLogs(AppLogLevel.Information, limit: 10);
        Assert.Contains(entries, entry => entry.Message == "Application startup completed");
    }

    [Fact]
    public void LoggingService_WritesEmergencyCrashDiagnosticsForFatalErrors()
    {
        var storagePaths = CreateStoragePaths();
        var loggingService = CreateLoggingService(storagePaths);

        loggingService.LogError(
            "Qdrant",
            "Connection refused while starting local runtime",
            new InvalidOperationException("Qdrant refused connection"));

        string crashFilePath = Path.Combine(storagePaths.LogsDirectory, "crash-diagnostics.log");
        Assert.True(File.Exists(crashFilePath));

        string content = File.ReadAllText(crashFilePath);
        Assert.Contains("Qdrant", content);
        Assert.Contains("Connection refused while starting local runtime", content);
    }

    private static LoggingService CreateLoggingService(AppStoragePaths storagePaths)
    {
        return new LoggingService(storagePaths, new LoggingSettingsStore(new TestSettingsRepository()));
    }

    private static AppStoragePaths CreateStoragePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "onlyrag-logging-tests", Guid.NewGuid().ToString("N"));
        return AppStoragePaths.FromRoot(root);
    }

    private sealed class TestSettingsRepository : ISettingsRepository
    {
        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class LoggingServiceTests : IDisposable
{
    private readonly string tempPath;
    private readonly AppStoragePaths storagePaths;
    private readonly LocalSqliteStoreDescriptor storeDescriptor;
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly LocalSqliteSchemaInitializer schemaInitializer;
    private readonly LocalSqliteStorageService storageService;
    private readonly SqliteSettingsRepository settingsRepository;
    private readonly LoggingSettingsStore settingsStore;
    private readonly LoggingService loggingService;

    public LoggingServiceTests()
    {
        tempPath = Path.Combine(Path.GetTempPath(), "OnlyRag_LogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        storagePaths = AppStoragePaths.FromRoot(tempPath);
        foreach (var dir in storagePaths.EnumerateRequiredDirectories())
        {
            Directory.CreateDirectory(dir);
        }

        storeDescriptor = new LocalSqliteStoreDescriptor(storagePaths);
        connectionFactory = new LocalSqliteConnectionFactory(storeDescriptor);
        schemaInitializer = new LocalSqliteSchemaInitializer(storeDescriptor, connectionFactory);
        storageService = new LocalSqliteStorageService(storeDescriptor, schemaInitializer);
        storageService.InitializeAsync().GetAwaiter().GetResult();

        settingsRepository = new SqliteSettingsRepository(connectionFactory);
        settingsStore = new LoggingSettingsStore(settingsRepository);
        loggingService = new LoggingService(storagePaths, settingsStore);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
        catch
        {
            // Temp folder cleanup
        }
    }

    [Fact]
    public async Task DefaultLogLevel_IsTrace()
    {
        var settings = await loggingService.GetSettingsAsync();
        Assert.Equal(AppLogLevel.Trace, settings.MinLevel);
    }

    [Fact]
    public async Task Logging_WritesAndRetrievesLogs()
    {
        loggingService.LogInfo("TestCategory", "Messaggio informativo di test");
        loggingService.LogError("TestCategory", "Errore di test", new InvalidOperationException("Eccezione simulata"));

        var logs = loggingService.GetRecentLogs();
        Assert.True(logs.Count >= 2);
        Assert.Contains(logs, l => l.Message.Contains("Messaggio informativo di test"));
        Assert.Contains(logs, l => l.Message.Contains("Errore di test"));
    }

    [Fact]
    public async Task UpdateSettings_ChangesMinLevelAndDisablesLogsWhenNone()
    {
        await loggingService.UpdateSettingsAsync(new LoggingSettings(AppLogLevel.None));
        var settings = await loggingService.GetSettingsAsync();
        Assert.Equal(AppLogLevel.None, settings.MinLevel);

        int countBefore = loggingService.GetRecentLogs().Count;
        loggingService.LogInfo("TestCategory", "Messaggio che non deve essere registrato");
        int countAfter = loggingService.GetRecentLogs().Count;

        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task GetStorageInfoAndClearLogs_WorksAsExpected()
    {
        loggingService.LogInfo("StorageTest", "Generazione contenuto per file log");

        var infoBefore = loggingService.GetStorageInfo();
        Assert.True(infoBefore.TotalSizeBytes >= 0);
        Assert.NotEmpty(infoBefore.FormattedSize);

        await loggingService.ClearLogsAsync();

        var infoAfter = loggingService.GetStorageInfo();
        Assert.Equal(0, infoAfter.MemoryEntryCount);
    }
}

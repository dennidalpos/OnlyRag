using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class SqliteMaintenanceServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly AppStoragePaths _storagePaths;
    private readonly LocalSqliteStoreDescriptor _descriptor;
    private readonly LocalSqliteConnectionFactory _connectionFactory;
    private readonly LocalSqliteSchemaInitializer _schemaInitializer;
    private readonly SqliteMaintenanceService _maintenanceService;

    public SqliteMaintenanceServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"onlyrag_maint_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _storagePaths = AppStoragePaths.FromRoot(_tempDirectory);

        foreach (string dir in _storagePaths.EnumerateRequiredDirectories())
        {
            Directory.CreateDirectory(dir);
        }

        _descriptor = new LocalSqliteStoreDescriptor(_storagePaths);
        _connectionFactory = new LocalSqliteConnectionFactory(_descriptor);
        _schemaInitializer = new LocalSqliteSchemaInitializer(_descriptor, _connectionFactory);
        _schemaInitializer.InitializeAsync().GetAwaiter().GetResult();

        _maintenanceService = new SqliteMaintenanceService(_connectionFactory, _descriptor);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsValidDatabaseStatus()
    {
        SqliteDatabaseStatusResponse status = await _maintenanceService.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status.Exists);
        Assert.True(status.FileSizeBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(status.FormattedFileSize));
        Assert.True(status.Fts5Enabled);
    }

    [Fact]
    public async Task RunMaintenanceAsync_ExecutesVacuumAndOptimizationSuccessfully()
    {
        SqliteMaintenanceResultResponse result = await _maintenanceService.RunMaintenanceAsync();

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.InitialFileSizeBytes > 0);
        Assert.True(result.FinalFileSizeBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        SqliteDatabaseStatusResponse statusAfter = await _maintenanceService.GetStatusAsync();
        Assert.NotNull(statusAfter.LastMaintenanceAtUtc);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }
}

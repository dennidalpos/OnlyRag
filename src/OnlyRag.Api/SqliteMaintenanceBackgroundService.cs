using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlyRag.Core;

namespace OnlyRag.Api;

public sealed class SqliteMaintenanceBackgroundService : BackgroundService
{
    private readonly ISqliteMaintenanceService _maintenanceService;
    private readonly ILogger<SqliteMaintenanceBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public SqliteMaintenanceBackgroundService(
        ISqliteMaintenanceService maintenanceService,
        ILogger<SqliteMaintenanceBackgroundService> logger)
    {
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay before first background maintenance run
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Starting scheduled SQLite background maintenance and FTS5 index compaction...");
                }

                var result = await _maintenanceService.RunMaintenanceAsync(stoppingToken);

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("SQLite background maintenance completed: {Message}", result.Message);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error occurred during scheduled SQLite background maintenance.");
            }

            await Task.Delay(Interval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

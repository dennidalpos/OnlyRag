using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlyRag.Core;

namespace OnlyRag.Api.Services;

public sealed class SyncBackgroundWorkerService : BackgroundService
{
    private readonly ILanSyncService lanSyncService;
    private readonly ILogger<SyncBackgroundWorkerService> logger;
    private readonly TimeSpan checkInterval = TimeSpan.FromSeconds(30);

    public SyncBackgroundWorkerService(
        ILanSyncService lanSyncService,
        ILogger<SyncBackgroundWorkerService> logger)
    {
        this.lanSyncService = lanSyncService;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        logger.LogInformation("Avvio worker di sincronizzazione delta LAN multi-dispositivo...");

        int consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await lanSyncService.AnnounceNodeBeaconAsync(stoppingToken);
                var discovered = await lanSyncService.DiscoverLanNodesAsync(stoppingToken);
                if (discovered.Count > 0 && logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Rilevati {Count} nodi LAN per la sincronizzazione.", discovered.Count);
                }

                consecutiveFailures = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                consecutiveFailures++;
                TimeSpan backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, consecutiveFailures), 60));
                logger.LogWarning(ex, "Errore nel ciclo di sincronizzazione LAN delta (tentativo fallito {Failures}). Retrying in {Backoff}s...", consecutiveFailures, backoff.TotalSeconds);
                try
                {
                    await Task.Delay(backoff, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                continue;
            }

            try
            {
                await Task.Delay(checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

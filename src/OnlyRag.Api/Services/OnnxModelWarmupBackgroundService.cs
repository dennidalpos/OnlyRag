using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api.Services;

public sealed class OnnxModelWarmupBackgroundService : BackgroundService
{
    private readonly IReRankerService reRankerService;
    private readonly IImageGenerationEngine imageGenerationEngine;
    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly ILogger<OnnxModelWarmupBackgroundService> logger;

    public OnnxModelWarmupBackgroundService(
        IReRankerService reRankerService,
        IImageGenerationEngine imageGenerationEngine,
        LocalSqliteStoreDescriptor descriptor,
        ILogger<OnnxModelWarmupBackgroundService> logger)
    {
        this.reRankerService = reRankerService;
        this.imageGenerationEngine = imageGenerationEngine;
        this.descriptor = descriptor;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield to allow HTTP server initialization before starting background warmup
        await Task.Yield();

        try
        {
            logger.LogInformation("Avvio warm-up asincrono modelli ONNX in background...");
            Task rerankerWarmup = reRankerService.WarmupAsync(stoppingToken);
            Task imageWarmup = imageGenerationEngine.WarmupAsync(descriptor.Paths.ImageModelsDirectory, preferGpu: true, stoppingToken);

            await Task.WhenAll(rerankerWarmup, imageWarmup);
            logger.LogInformation("Warm-up asincrono modelli ONNX completato con successo.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Eccezione durante il warm-up dei modelli ONNX (l'applicazione continua regolarmente).");
        }
    }
}

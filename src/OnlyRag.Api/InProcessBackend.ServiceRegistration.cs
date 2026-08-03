using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Api.Ollama;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Export;
using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Retrieval.Graph;
using OnlyRag.Infrastructure.Agent.Memory;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal static class InProcessBackendServiceRegistration
{
    public static IServiceCollection AddOnlyRagBackendServices(
        this IServiceCollection services,
        InProcessBackendDescriptor descriptor,
        InProcessBackendOptions options,
        BackendRuntimeState runtimeState)
    {
        return services
            .AddOnlyRagRuntimeServices(descriptor, options, runtimeState)
            .AddOnlyRagStorageServices()
            .AddOnlyRagRetrievalServices(options)
            .AddOnlyRagDocumentServices()
            .AddOnlyRagOcrServices()
            .AddOnlyRagSettingsAndDiagnosticsServices()
            .AddOnlyRagImageServices(options)
            .AddOnlyRagJobServices();
    }

    public static IServiceCollection AddOnlyRagHttpApiOptions(
        this IServiceCollection services,
        InProcessBackendOptions options,
        string corsPolicyName,
        string[] allowedCorsOrigins)
    {
        services.Configure<FormOptions>(formOptions =>
        {
            formOptions.MultipartBodyLengthLimit = options.DocumentLibraryLimits.MaxRequestBodySizeBytes;
            formOptions.MemoryBufferThreshold = 1024 * 64;
        });
        services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.AddCors(cors =>
        {
            cors.AddPolicy(corsPolicyName, policy =>
            {
                policy
                    .WithOrigins(allowedCorsOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }

    private static IServiceCollection AddOnlyRagRuntimeServices(
        this IServiceCollection services,
        InProcessBackendDescriptor descriptor,
        InProcessBackendOptions options,
        BackendRuntimeState runtimeState)
    {
        services.AddSingleton(descriptor);
        services.AddSingleton(descriptor.StoragePaths);
        services.AddSingleton(descriptor.Store);
        services.AddSingleton(descriptor.JobQueue);
        services.AddSingleton(options.DocumentLibraryLimits);
        services.AddSingleton<ILocalProcessLauncher>(options.ProcessLauncher ?? new LocalProcessLauncher());
        services.AddSingleton(runtimeState);
        services.AddSingleton<ApplicationShutdownService>();
        return services;
    }

    private static IServiceCollection AddOnlyRagStorageServices(this IServiceCollection services)
    {
        services.AddSingleton<ISqliteConnectionFactory, LocalSqliteConnectionFactory>();
        services.AddSingleton<LocalSqliteSchemaInitializer>();
        services.AddSingleton<ILocalStorageService, LocalSqliteStorageService>();
        services.AddSingleton<IDocumentRepository, SqliteDocumentRepository>();
        services.AddSingleton<IEmbeddingRepository, SqliteEmbeddingRepository>();
        services.AddSingleton<IChatHistoryRepository, SqliteChatHistoryRepository>();
        services.AddSingleton<ITranslationRepository, SqliteTranslationRepository>();
        services.AddSingleton<IGeneratedImageRepository, SqliteGeneratedImageRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        return services;
    }

    private static IServiceCollection AddOnlyRagRetrievalServices(
        this IServiceCollection services,
        InProcessBackendOptions options)
    {
        services.AddSingleton<QdrantSettingsStore>();
        if (options.QdrantVectorStore is not null)
        {
            services.AddSingleton(options.QdrantVectorStore);
        }
        else
        {
            services.AddSingleton<IQdrantVectorStore, QdrantVectorStore>();
        }

        services.AddSingleton<QdrantLocalRuntimeService>();
        services.AddSingleton(HybridRetrievalOptions.Default);
        services.AddSingleton<IKeywordSearchService, SqliteKeywordSearchService>();
        services.AddSingleton<IRetrievalChunkRepository, SqliteRetrievalChunkRepository>();
        services.AddSingleton<IQueryEmbeddingGenerator, OllamaQueryEmbeddingGenerator>();
        services.AddSingleton<HeuristicReRankerService>();
        services.AddSingleton<RerankerModelManager>();
        services.AddSingleton<IReRankerService, OnnxCrossEncoderReRankerService>();
        services.AddSingleton<ILlmQueryExpander, OllamaLlmQueryExpander>();
        services.AddSingleton<IQueryTransformationService, OllamaQueryTransformationService>();
        services.AddSingleton<ParentChildChunkResolver>();
        services.AddSingleton<CragDecisionEngine>();
        services.AddSingleton<IEntityGraphExtractor, EntityGraphExtractor>();
        services.AddSingleton<IGraphRetrievalService, SqliteGraphRetrievalService>();
        services.AddSingleton<IAgentEpisodicMemoryService, SqliteQdrantEpisodicMemoryService>();
        services.AddSingleton<IAgentSkillRepository, SqliteAgentSkillRepository>();
        services.AddSingleton<IAgentSkillAutoLearner, AgentSkillAutoLearner>();
        services.AddSingleton<ISubagentReportCacheRepository, SqliteSubagentReportCacheRepository>();
        services.AddSingleton<IWorkspaceVectorIndexerService, WorkspaceVectorIndexerService>();
        services.AddSingleton<IAstDependencyGraphService, AstDependencyGraphService>();
        services.AddSingleton<IGraphRagAstSymbolIndexer, GraphRagAstSymbolIndexer>();
        services.AddSingleton<WorkspaceSnapshotCheckpointManager>();
        services.AddSingleton<IHybridRetrievalService, HybridRetrievalService>();
        services.AddSingleton<IRetrievalBenchmarkReportService, RetrievalBenchmarkReportService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<BackgroundTaskManager>();
        services.AddSingleton<ISubagentRunner, SubagentRunner>();
        services.AddSingleton<WorkspaceToolExecutor>();
        services.AddTransient<AgentLoopEngine>();
        return services;
    }

    private static IServiceCollection AddOnlyRagDocumentServices(this IServiceCollection services)
    {
        services.AddSingleton<TranslationExportService>();
        services.AddSingleton<IDocumentLibraryService, LocalDocumentLibraryService>();
        services.AddSingleton<LocalDocumentStorageGuard>();
        services.AddSingleton<IngestionSettingsStore>();
        services.AddSingleton<DocumentTextChunker>();
        services.AddSingleton<OfficeOpenXmlTextExtractor>();
        services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();
        return services;
    }

    private static IServiceCollection AddOnlyRagOcrServices(this IServiceCollection services)
    {
        services.AddSingleton<IOcrCacheRepository, SqliteOcrCacheRepository>();
        services.AddSingleton<OcrSettingsStore>();
        services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
        services.AddSingleton<OcrRetryPolicy>();
        services.AddSingleton<OcrStartupAnalysisService>();
        services.AddSingleton<OcrGpuCapabilityService>();
        return services;
    }

    private static IServiceCollection AddOnlyRagSettingsAndDiagnosticsServices(this IServiceCollection services)
    {
        services.AddSingleton<IOllamaSettingsService, OllamaSettingsService>();
        services.AddSingleton<IPerformanceSettingsService, PerformanceSettingsService>();
        services.AddSingleton<PdfExportSettingsStore>();
        services.AddSingleton<OnlyRag.Infrastructure.Logging.LoggingSettingsStore>();
        services.AddSingleton<OnlyRag.Infrastructure.Logging.ILoggingService, OnlyRag.Infrastructure.Logging.LoggingService>();
        services.AddSingleton<OllamaGenerationCoordinator>();
        services.AddSingleton<DependencyProvisioningService>();
        services.AddSingleton<DiagnosticsProbeCacheService>();
        services.AddSingleton<SystemTelemetryService>();
        services.AddHttpClient<IOllamaClient, OllamaClient>();
        return services;
    }

    private static IServiceCollection AddOnlyRagImageServices(
        this IServiceCollection services,
        InProcessBackendOptions options)
    {
        services.AddSingleton<ImageModelCatalogStore>();
        services.AddSingleton<IImageGenerationSettingsService, ImageGenerationSettingsService>();
        services.AddSingleton(options.ImageGenerationEngine ?? new OnnxStableDiffusionImageGenerationEngine());
        services.AddSingleton<ImageGenerationService>();
        services.AddHttpClient<ImageModelManager>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        return services;
    }

    private static IServiceCollection AddOnlyRagJobServices(this IServiceCollection services)
    {
        services.AddSingleton<ILocalJobQueue, SqliteLocalJobQueue>();
        services.AddSingleton<ILocalJobHandler, DocumentIngestionJobHandler>();
        services.AddSingleton<ILocalJobHandler, DocumentEmbeddingJobHandler>();
        services.AddSingleton<ILocalJobHandler, DocumentTranslationJobHandler>();
        services.AddSingleton<ILocalJobHandler, OllamaModelPullJobHandler>();
        services.AddSingleton<RunningJobCancellationRegistry>();
        services.AddHostedService<LocalJobWorkerService>();
        return services;
    }
}

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Api.Ollama;
using OnlyRag.Api.Services;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Ai;
using OnlyRag.Infrastructure.Export;

using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Retrieval.Graph;
using OnlyRag.Infrastructure.Agent.Memory;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Storage.EF;
using OnlyRag.Infrastructure.Storage.Security;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Infrastructure.Vram;
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
                    .AllowAnyMethod()
                    .AllowCredentials(); // Required for SignalR WebSocket handshake
            });
        });

        services.AddOpenApi();
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
        services.AddSingleton<ISqliteKeyProvider, WindowsCredentialManagerSqliteKeyProvider>();
        services.AddSingleton<ISqliteConnectionFactory, LocalSqliteConnectionFactory>();
        services.AddDbContext<OnlyRagDbContext>((sp, opts) =>
        {
            var descriptor = sp.GetRequiredService<InProcessBackendDescriptor>();
            var keyProvider = sp.GetRequiredService<ISqliteKeyProvider>();
            string dbKey = keyProvider.GetOrCreateDatabaseKey();
            var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = descriptor.StoragePaths.DatabasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                Password = dbKey
            }.ToString();
            opts.UseSqlite(connectionString);
        });
        services.AddSingleton<LocalSqliteSchemaInitializer>();
        services.AddSingleton<ILocalStorageService, LocalSqliteStorageService>();
        services.AddSingleton<IDocumentRepository, SqliteDocumentRepository>();
        services.AddSingleton<IEmbeddingRepository, SqliteEmbeddingRepository>();
        services.AddSingleton<IChatHistoryRepository, SqliteChatHistoryRepository>();
        services.AddSingleton<ITranslationRepository, SqliteTranslationRepository>();
        services.AddSingleton<IGeneratedImageRepository, SqliteGeneratedImageRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IAgentRunStateRepository, SqliteAgentRunStateRepository>();
        services.AddSingleton<SqlitePolicyAuditRepository>();
        services.AddSingleton<IAesBackupService, AesBackupService>();
        services.AddSingleton<ICloudApiKeyVault, WindowsCredentialManagerCloudKeyVault>();
        services.AddSingleton<ICloudLlmClientFactory, CloudLlmClientFactory>();
        services.AddSingleton<ISqliteMaintenanceService, SqliteMaintenanceService>();
        services.AddHostedService<SqliteMaintenanceBackgroundService>();
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
        services.AddSingleton<OnlyRag.Core.IQdrantSyncRepairService, OnlyRag.Infrastructure.Vector.QdrantSyncRepairService>();
        services.AddSingleton(HybridRetrievalOptions.Default);
        services.AddSingleton<IKeywordSearchService, SqliteKeywordSearchService>();
        services.AddSingleton<IRetrievalChunkRepository, SqliteRetrievalChunkRepository>();
        services.AddSingleton<IQueryEmbeddingGenerator, OllamaQueryEmbeddingGenerator>();
        services.AddSingleton<HeuristicReRankerService>();
        services.AddSingleton<RerankerModelManager>();
        services.AddSingleton<IReRankerService, OnnxCrossEncoderReRankerService>();
        services.AddSingleton<IQueryIntentClassifierService, QueryIntentClassifierService>();
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
        services.AddSingleton<IAgentMctsCheckpointRepository, SqliteAgentMctsCheckpointRepository>();
        services.AddSingleton<IAgentQueryIntentRouter, AgentQueryIntentRouter>();
        services.AddSingleton<IAgentVerificationEngine, AgentVerificationEngine>();
        services.AddSingleton<IMultiAgentOrchestratorService, MultiAgentOrchestratorService>();
        services.AddSingleton<ILanSyncService, OnlyRag.Infrastructure.Sync.LanSyncService>();
        services.AddHostedService<SyncBackgroundWorkerService>();
        services.AddSingleton<IWorkspaceVectorIndexerService, WorkspaceVectorIndexerService>();
        services.AddSingleton<IAstDependencyGraphService, AstDependencyGraphService>();
        services.AddSingleton<IGraphRagAstSymbolIndexer, GraphRagAstSymbolIndexer>();
        services.AddSingleton<WorkspaceSnapshotCheckpointManager>();
        services.AddSingleton<OnlyRag.Infrastructure.Agent.Memory.EpisodicMemoryIndexer>();
        services.AddSingleton<IAgentExecutionPolicyService, AgentExecutionPolicyService>();
        services.AddSingleton<IHybridRetrievalService, HybridRetrievalService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<BackgroundTaskManager>();
        services.AddSingleton<ISubagentRunner, SubagentRunner>();
        services.AddSingleton<WorkspaceToolExecutor>();
        services.AddHttpClient<OnlyRag.Core.Mcp.IMcpSseClient, OnlyRag.Api.Mcp.McpSseClientService>();
        services.AddSingleton<OnlyRag.Core.Mcp.IMcpClientService, OnlyRag.Api.Mcp.McpClientService>();
        services.AddTransient<AgentLoopEngine>();
        return services;
    }

    private static IServiceCollection AddOnlyRagDocumentServices(this IServiceCollection services)
    {
        services.AddSingleton<TranslationExportService>();
        services.AddSingleton<OnlyRag.Core.IChatReportExportService, OnlyRag.Infrastructure.Export.ChatReportExportService>();
        services.AddSingleton<IDocumentLibraryService, LocalDocumentLibraryService>();
        services.AddSingleton<IArchiveManifestRepository, SqliteArchiveManifestRepository>();
        services.AddSingleton<IBatchIngestionQueueService, BatchIngestionQueueService>();
        services.AddSingleton<LocalDocumentStorageGuard>();
        services.AddSingleton<IngestionSettingsStore>();
        services.AddSingleton<ArchiveExtractionService>();
        services.AddSingleton<DocumentTextChunker>();
        services.AddSingleton<OfficeOpenXmlTextExtractor>();
        services.AddSingleton<IStreamingDocumentIngestionPipeline, StreamingDocumentIngestionPipeline>();
        services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();
        return services;
    }

    private static IServiceCollection AddOnlyRagOcrServices(this IServiceCollection services)
    {
        services.AddSingleton<IOcrCacheRepository, SqliteOcrCacheRepository>();
        services.AddSingleton<OcrSettingsStore>();
        services.AddSingleton<PaddleOcrEngine>();
        services.AddSingleton<IOcrEngine>(sp => sp.GetRequiredService<PaddleOcrEngine>());
        services.AddSingleton<OnnxDirectMlOcrEngine>();
        services.AddSingleton<OcrRetryPolicy>();
        services.AddSingleton<OcrStartupAnalysisService>();
        services.AddSingleton<OcrGpuCapabilityService>();
        return services;
    }

    private static IServiceCollection AddOnlyRagSettingsAndDiagnosticsServices(this IServiceCollection services)
    {
        services.AddSingleton<IOllamaSettingsService, OllamaSettingsService>();
        services.AddSingleton<OllamaQueryEmbeddingCache>();
        services.AddSingleton<IOllamaLoadBalancer, OllamaLoadBalancer>();
        services.AddSingleton<IPerformanceSettingsService, PerformanceSettingsService>();
        services.AddSingleton<OnlyRag.Core.IHardwareMonitorService, OnlyRag.Infrastructure.Hardware.HardwareMonitorService>();
        services.AddSingleton<PdfExportSettingsStore>();
        services.AddSingleton<OnlyRag.Infrastructure.Logging.LoggingSettingsStore>();
        services.AddSingleton<OnlyRag.Infrastructure.Logging.LoggingService>();
        services.AddSingleton<OnlyRag.Infrastructure.Logging.ILoggingService>(sp => sp.GetRequiredService<OnlyRag.Infrastructure.Logging.LoggingService>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(sp =>
            new OnlyRag.Infrastructure.Logging.OnlyRagLoggerProvider(sp.GetRequiredService<OnlyRag.Infrastructure.Logging.ILoggingService>()));
        services.AddSingleton<OllamaGenerationCoordinator>();
        services.AddSingleton<DependencyProvisioningService>();
        services.AddSingleton<DiagnosticsProbeCacheService>();
        services.AddSingleton<SystemTelemetryService>();
        services.AddSingleton<StartupTracer>();
        services.AddHttpClient<IOllamaClient, OllamaClient>();
        services.AddSignalR();

        services.AddTransient<Microsoft.Extensions.AI.IChatClient>(sp =>
        {
            var settingsService = sp.GetService<IOllamaSettingsService>();
            var httpClient = sp.GetService<HttpClient>() ?? new HttpClient();
            var endpoint = "http://127.0.0.1:11434";
            var model = "llama3";
            if (settingsService is not null)
            {
                try
                {
                    var settings = settingsService.GetAsync().GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(settings.OllamaBaseUrl)) endpoint = settings.OllamaBaseUrl;
                    if (!string.IsNullOrWhiteSpace(settings.DefaultChatModel)) model = settings.DefaultChatModel;
                }
                catch { }
            }
            return OllamaLocalClientFactory.CreateChatClient(httpClient, endpoint, model);
        });

        services.AddTransient<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(sp =>
        {
            var settingsService = sp.GetService<IOllamaSettingsService>();
            var httpClient = sp.GetService<HttpClient>() ?? new HttpClient();
            var endpoint = "http://127.0.0.1:11434";
            var model = "nomic-embed-text";
            if (settingsService is not null)
            {
                try
                {
                    var settings = settingsService.GetAsync().GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(settings.OllamaBaseUrl)) endpoint = settings.OllamaBaseUrl;
                    if (!string.IsNullOrWhiteSpace(settings.DefaultEmbeddingModel)) model = settings.DefaultEmbeddingModel;
                }
                catch { }
            }
            return OllamaLocalClientFactory.CreateEmbeddingGenerator(httpClient, endpoint, model);
        });

        services.AddSingleton<IStreamingEmbeddingGenerator, MicrosoftExtensionsAiEmbeddingGeneratorAdapter>();

        return services;
    }

    private static IServiceCollection AddOnlyRagImageServices(
        this IServiceCollection services,
        InProcessBackendOptions options)
    {
        services.AddSingleton<IVramMemoryManager, VramMemoryManager>();
        services.AddSingleton<ImageModelCatalogStore>();
        services.AddSingleton<IImageGenerationSettingsService, ImageGenerationSettingsService>();
        services.AddSingleton<IImageGenerationEngine>(sp => options.ImageGenerationEngine ?? new OnnxStableDiffusionImageGenerationEngine(sp.GetService<IVramMemoryManager>()));
        services.AddSingleton<ImageGenerationService>();
        services.AddHttpClient<ImageModelManager>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        return services;
    }

    private static IServiceCollection AddOnlyRagJobServices(this IServiceCollection services)
    {
        services.AddSingleton<Worker.IJobProgressNotifier, SignalRJobProgressNotifier>();
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

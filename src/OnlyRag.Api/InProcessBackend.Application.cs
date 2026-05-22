using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private const string WebViewCorsPolicy = "OnlyRagWebView";

    private static WebApplication BuildApplication(
        InProcessBackendDescriptor descriptor,
        InProcessBackendOptions options,
        BackendRuntimeState runtimeState,
        string sessionToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(InProcessBackend).Assembly.GetName().Name,
            Args = []
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(options.Address, options.Port);
            kestrel.Limits.MaxRequestBodySize = options.DocumentLibraryLimits.MaxRequestBodySizeBytes;
        });

        builder.Services.AddSingleton(descriptor);
        builder.Services.AddSingleton(descriptor.Store);
        builder.Services.AddSingleton(descriptor.JobQueue);
        builder.Services.AddSingleton(options.DocumentLibraryLimits);
        builder.Services.AddSingleton<ILocalProcessLauncher>(options.ProcessLauncher ?? new LocalProcessLauncher());
        builder.Services.AddSingleton(runtimeState);
        builder.Services.AddSingleton<ISqliteConnectionFactory, LocalSqliteConnectionFactory>();
        builder.Services.AddSingleton<LocalSqliteMigrator>();
        builder.Services.AddSingleton<ILocalStorageService, LocalSqliteStorageService>();
        builder.Services.AddSingleton<IDocumentRepository, SqliteDocumentRepository>();
        builder.Services.AddSingleton<IEmbeddingRepository, SqliteEmbeddingRepository>();
        builder.Services.AddSingleton<SqliteVecVectorSearchService>();
        builder.Services.AddSingleton<IVectorSearchService>(services => services.GetRequiredService<SqliteVecVectorSearchService>());
        builder.Services.AddSingleton(HybridRetrievalOptions.Default);
        builder.Services.AddSingleton<IKeywordSearchService, SqliteKeywordSearchService>();
        builder.Services.AddSingleton<IRetrievalChunkRepository, SqliteRetrievalChunkRepository>();
        builder.Services.AddSingleton<IQueryEmbeddingGenerator, OllamaQueryEmbeddingGenerator>();
        builder.Services.AddSingleton<IHybridRetrievalService, HybridRetrievalService>();
        builder.Services.AddSingleton<IChatHistoryRepository, SqliteChatHistoryRepository>();
        builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton<ITranslationRepository, SqliteTranslationRepository>();
        builder.Services.AddSingleton<TranslationExportService>();
        builder.Services.AddSingleton<IDocumentLibraryService, LocalDocumentLibraryService>();
        builder.Services.AddSingleton<LocalDocumentStorageGuard>();
        builder.Services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        builder.Services.AddSingleton<IOcrCacheRepository, SqliteOcrCacheRepository>();
        builder.Services.AddSingleton<OcrSettingsStore>();
        builder.Services.AddSingleton<OcrProcessingSettingsStore>();
        builder.Services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
        builder.Services.AddSingleton<OcrRetryPolicy>();
        builder.Services.AddSingleton<IngestionSettingsStore>();
        builder.Services.AddSingleton<OfficeConversionSettingsStore>();
        builder.Services.AddSingleton<IOfficeConversionService, LibreOfficeConversionService>();
        builder.Services.AddSingleton<IOllamaSettingsService, OllamaSettingsService>();
        builder.Services.AddSingleton<IPerformanceSettingsService, PerformanceSettingsService>();
        builder.Services.AddSingleton<DependencyProvisioningService>();
        builder.Services.AddSingleton<OcrStartupAnalysisService>();
        builder.Services.AddSingleton<OcrGpuCapabilityService>();
        builder.Services.AddSingleton<SystemTelemetryService>();
        builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();
        builder.Services.AddSingleton<ILocalJobQueue, SqliteLocalJobQueue>();
        builder.Services.AddSingleton<DocumentTextChunker>();
        builder.Services.AddSingleton<OfficeOpenXmlTextExtractor>();
        builder.Services.AddSingleton<IDocumentIngestionService, DocumentIngestionService>();
        builder.Services.AddSingleton<ILocalJobHandler, DocumentIngestionJobHandler>();
        builder.Services.AddSingleton<ILocalJobHandler, DocumentEmbeddingJobHandler>();
        builder.Services.AddSingleton<ILocalJobHandler, DocumentTranslationJobHandler>();
        builder.Services.AddSingleton<RunningJobCancellationRegistry>();
        builder.Services.AddSingleton<ApplicationShutdownService>();
        builder.Services.AddHostedService<LocalJobWorkerService>();
        builder.Services.Configure<FormOptions>(formOptions =>
        {
            formOptions.MultipartBodyLengthLimit = options.DocumentLibraryLimits.MaxRequestBodySizeBytes;
            formOptions.MemoryBufferThreshold = 1024 * 64;
        });
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        builder.Services.AddCors(cors =>
        {
            cors.AddPolicy(WebViewCorsPolicy, policy =>
            {
                policy
                    .WithOrigins(ResolveAllowedCorsOrigins(options))
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        WebApplication app = builder.Build();

        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                string correlationId = context.TraceIdentifier;
                if (exceptionFeature?.Error is Exception exception)
                {
                    var appDescriptor = context.RequestServices.GetRequiredService<InProcessBackendDescriptor>();
                    BackendLog.WriteException(appDescriptor.StoragePaths, correlationId, "Unhandled API exception.", exception);
                }

                await WriteProblemAsync(
                    context,
                    "Errore interno del server.",
                    CreateUnexpectedErrorDetail(correlationId),
                    StatusCodes.Status500InternalServerError,
                    "unexpected_error",
                    correlationId);
            });
        });
        app.UseCors(WebViewCorsPolicy);
        UseSessionTokenAuthentication(app, sessionToken);
        MapEndpoints(app);

        return app;
    }

    private static string[] ResolveAllowedCorsOrigins(InProcessBackendOptions options)
    {
        List<string> origins = [OnlyRagWebOrigins.StaticWebViewOrigin];
        if (options.EnableDevelopmentCorsOrigins)
        {
            origins.Add("http://127.0.0.1:5173");
            origins.Add("http://localhost:5173");
        }

        return origins.ToArray();
    }

    private static void MapEndpoints(WebApplication app)
    {
        MapAppEndpoints(app);
        MapRetrievalEndpoints(app);
        MapSettingsEndpoints(app);
        MapDependencyEndpoints(app);
        MapJobEndpoints(app);
        MapDocumentEndpoints(app);
        MapTranslationEndpoints(app);
    }

    private static Uri ResolveBaseUri(IHost app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();
        string? address = addresses?.Addresses.FirstOrDefault();

        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("The in-process backend started without a resolvable listening address.");
        }

        return uri;
    }
}

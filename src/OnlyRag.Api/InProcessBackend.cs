using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private const string WebViewCorsPolicy = "OnlyRagWebView";

    public static async Task<InProcessBackendHandle> StartAsync(
        InProcessBackendDescriptor? descriptor = null,
        InProcessBackendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        descriptor ??= InProcessBackendDescriptor.CreateDefault();
        options ??= new InProcessBackendOptions();

        if (!IPAddress.IsLoopback(options.Address))
        {
            throw new InvalidOperationException("OnlyRag in-process backend can only bind to a loopback address.");
        }

        Directory.CreateDirectory(descriptor.StoragePaths.DataRoot);
        BackendLog.Write(descriptor.StoragePaths, "Starting in-process backend.");

        string sessionToken = ResolveSessionToken(options);
        var runtimeState = new BackendRuntimeState(DateTimeOffset.UtcNow);
        WebApplication app = BuildApplication(descriptor, options, runtimeState, sessionToken);

        try
        {
            StorageStatusResponse storageStatus = await app.Services
                .GetRequiredService<ILocalStorageService>()
                .InitializeAsync(cancellationToken);
            runtimeState.DatabaseStatus = storageStatus.MigrationStatus;
            BackendLog.Write(descriptor.StoragePaths, $"Local SQLite schema version {storageStatus.CurrentSchemaVersion}/{storageStatus.TargetSchemaVersion}: {storageStatus.MigrationStatus}.");

            int recoveredJobs = await app.Services
                .GetRequiredService<ILocalJobQueue>()
                .RecoverInterruptedJobsAsync(cancellationToken);
            if (recoveredJobs > 0)
            {
                BackendLog.Write(descriptor.StoragePaths, $"Recovered {recoveredJobs} interrupted job(s).");
            }

            await app.Services
                .GetRequiredService<SqliteVecVectorSearchService>()
                .VerifyAvailabilityAsync(cancellationToken);
            BackendLog.Write(descriptor.StoragePaths, "sqlite-vec native extension verified.");

            await app.StartAsync(cancellationToken);
            Uri baseUri = ResolveBaseUri(app);
            runtimeState.BaseUri = baseUri;
            BackendLog.Write(descriptor.StoragePaths, $"In-process backend listening on {baseUri}.");

            return new InProcessBackendHandle(app, baseUri, descriptor, sessionToken);
        }
        catch (Exception ex)
        {
            BackendLog.Write(descriptor.StoragePaths, $"In-process backend failed to start: {ex.Message}");
            await app.DisposeAsync();
            throw;
        }
    }

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

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Errore interno del server.",
                    detail = CreateUnexpectedErrorDetail(correlationId),
                    status = 500
                });
            });
        });
        app.UseCors(WebViewCorsPolicy);
        UseSessionTokenAuthentication(app, sessionToken);
        MapEndpoints(app);

        return app;
    }

    private static string ResolveSessionToken(InProcessBackendOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SessionToken))
        {
            return options.SessionToken;
        }

        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
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

    private static void UseSessionTokenAuthentication(WebApplication app, string sessionToken)
    {
        app.Use(async (context, next) =>
        {
            if (IsHealthRequest(context.Request) || !context.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            if (IsValidSessionToken(context.Request, sessionToken))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Non autorizzato",
                detail = "Token di sessione API mancante o non valido.",
                status = StatusCodes.Status401Unauthorized
            });
        });
    }

    private static bool IsHealthRequest(HttpRequest request)
    {
        return request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidSessionToken(HttpRequest request, string sessionToken)
    {
        if (!request.Headers.TryGetValue(OnlyRagApiHeaders.SessionTokenHeaderName, out var values)
            || values.Count != 1)
        {
            return false;
        }

        string? suppliedToken = values[0];
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        byte[] suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(sessionToken);
        return suppliedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
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

    private static bool IsOcrCandidate(string? fileExtension)
    {
        return fileExtension?.ToLowerInvariant() is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".gif" or ".webp";
    }

    private static async Task<LocalJob?> GetActiveDocumentJobAsync(
        ImportedDocument document,
        ILocalJobQueue jobs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.CurrentJobId))
        {
            return null;
        }

        LocalJob? currentJob = await jobs.GetAsync(document.CurrentJobId, cancellationToken);
        return currentJob?.Status is JobStatus.Pending or JobStatus.Running or JobStatus.Pausing or JobStatus.Paused
            ? currentJob
            : null;
    }

    private static async Task<DocumentEmbeddingStatusResponse> BuildEmbeddingStatusResponseAsync(
        long documentId,
        string? model,
        IDocumentLibraryService documents,
        IEmbeddingRepository embeddings,
        ILocalJobQueue jobs,
        IVectorSearchService vectorSearch,
        CancellationToken cancellationToken)
    {
        ImportedDocument? document = await documents.GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException("Documento non trovato.");
        }

        DocumentEmbeddingStatusSnapshot snapshot = await embeddings.GetDocumentEmbeddingStatusAsync(
            documentId,
            model,
            cancellationToken);

        LocalJob? currentJob = null;
        if (!string.IsNullOrWhiteSpace(document.CurrentJobId))
        {
            LocalJob? job = await jobs.GetAsync(document.CurrentJobId, cancellationToken);
            if (job?.Type == DocumentEmbeddingJobHandler.DocumentEmbeddingJobType)
            {
                currentJob = job;
            }
        }

        int progressPercent = snapshot.ChunkCount == 0
            ? 0
            : (int)Math.Round(snapshot.EmbeddedChunkCount * 100d / snapshot.ChunkCount);
        if (currentJob is not null)
        {
            progressPercent = Math.Max(progressPercent, currentJob.ProgressPercent);
        }

        string state = ResolveEmbeddingState(model, snapshot, currentJob);

        return new DocumentEmbeddingStatusResponse(
            documentId,
            state,
            string.IsNullOrWhiteSpace(model) ? null : model,
            snapshot.ChunkCount,
            snapshot.EmbeddedChunkCount,
            Math.Clamp(progressPercent, 0, 100),
            currentJob?.Id,
            currentJob?.CurrentStep,
            vectorSearch.BackendName,
            snapshot.LastEmbeddedAtUtc);
    }

    private static async Task<TranslationDetailResponse?> BuildTranslationDetailAsync(
        long translationId,
        ITranslationRepository translations,
        CancellationToken cancellationToken)
    {
        StoredTranslation? translation = await translations.GetAsync(translationId, cancellationToken);
        if (translation is null)
        {
            return null;
        }

        IReadOnlyList<StoredTranslationUnit> units = await translations.ListUnitsPreviewAsync(
            translationId,
            take: 80,
            cancellationToken);
        return new TranslationDetailResponse(
            translation.ToResponse(),
            units.Select(unit => unit.ToResponse()).ToArray());
    }

    private static async Task<TranslationCompareResponse?> BuildTranslationCompareAsync(
        long translationId,
        int? requestedPage,
        ITranslationRepository translations,
        CancellationToken cancellationToken)
    {
        StoredTranslation? translation = await translations.GetAsync(translationId, cancellationToken);
        if (translation is null)
        {
            return null;
        }

        int[] pages = (await translations.ListUnitPagesAsync(
            translationId,
            cancellationToken)).ToArray();
        if (pages.Length == 0)
        {
            return new TranslationCompareResponse(
                translation.ToResponse(),
                CurrentPage: 1,
                PagePosition: 0,
                PageCount: 0,
                PreviousPage: null,
                NextPage: null,
                Units: []);
        }

        int currentPage = requestedPage.HasValue && pages.Contains(requestedPage.Value)
            ? requestedPage.Value
            : pages[0];
        int pageIndex = Array.IndexOf(pages, currentPage);
        IReadOnlyList<StoredTranslationUnit> pageUnits = await translations.ListUnitsByPageAsync(
            translationId,
            currentPage,
            cancellationToken);
        TranslationUnitResponse[] unitResponses = pageUnits
            .Select(unit => unit.ToResponse())
            .ToArray();

        return new TranslationCompareResponse(
            translation.ToResponse(),
            currentPage,
            pageIndex + 1,
            pages.Length,
            pageIndex > 0 ? pages[pageIndex - 1] : null,
            pageIndex < pages.Length - 1 ? pages[pageIndex + 1] : null,
            unitResponses);
    }

    private static async Task EnsureOllamaModelInstalledAsync(
        IOllamaClient ollamaClient,
        string model,
        string usage,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
        bool installed = models.Any(installedModel =>
            string.Equals(installedModel.Name, model, StringComparison.OrdinalIgnoreCase)
            || string.Equals(installedModel.Model, model, StringComparison.OrdinalIgnoreCase));
        if (!installed)
        {
            throw new OllamaApiException(
                OllamaErrorKind.ModelNotFound,
                $"Il modello {usage} '{model}' non e installato in Ollama.");
        }
    }

    private static string ResolveEmbeddingState(
        string? model,
        DocumentEmbeddingStatusSnapshot snapshot,
        LocalJob? currentJob)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "NotConfigured";
        }

        if (currentJob is not null)
        {
            return currentJob.Status.ToString();
        }

        if (snapshot.ChunkCount == 0)
        {
            return "NotIndexed";
        }

        if (snapshot.EmbeddedChunkCount == 0)
        {
            return "NotStarted";
        }

        return snapshot.EmbeddedChunkCount >= snapshot.ChunkCount ? "Complete" : "Partial";
    }

    private static IResult MapOllamaException(OllamaApiException exception)
    {
        return exception.Kind switch
        {
            OllamaErrorKind.InvalidUrl => Results.Problem(
                title: "URL Ollama non valido",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest),
            OllamaErrorKind.InvalidRequest => Results.Problem(
                title: "Richiesta Ollama non valida",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest),
            OllamaErrorKind.ModelNotFound => Results.Problem(
                title: "Modello assente",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound),
            OllamaErrorKind.Timeout => Results.Problem(
                title: "Timeout Ollama",
                detail: exception.Message,
                statusCode: StatusCodes.Status408RequestTimeout),
            OllamaErrorKind.Unreachable => Results.Problem(
                title: "Ollama non raggiungibile",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Problem(
                title: "Errore Ollama",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway)
        };
    }

    private static IResult MapOfficeConversionException(OfficeConversionException exception)
    {
        return Results.Problem(
            title: "Configurazione convertitore Office non valida",
            detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult CreateUnexpectedErrorProblem(string title, string? correlationId = null)
    {
        return Results.Problem(
            title: title,
            detail: CreateUnexpectedErrorDetail(correlationId),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static string CreateUnexpectedErrorDetail(string? correlationId = null)
    {
        return string.IsNullOrWhiteSpace(correlationId)
            ? "Si e verificato un errore imprevisto. I dettagli sono stati registrati nei log locali."
            : $"Si e verificato un errore imprevisto. I dettagli sono stati registrati nei log locali con riferimento {correlationId}.";
    }

    private static OfficeConverterStatusResponse CreateOfficeConverterStatusResponse(
        OfficeConverterAvailability availability,
        int timeoutSeconds)
    {
        return new OfficeConverterStatusResponse(
            availability.IsAvailable ? "Available" : "RequiresAdditionalComponent",
            availability.IsAvailable,
            availability.ExecutablePath,
            availability.Message,
            availability.Suggestion,
            timeoutSeconds);
    }

    private static OllamaStatusResponse CreateStatusResponse(string baseUrl, OllamaApiException exception)
    {
        return exception.Kind switch
        {
            OllamaErrorKind.InvalidUrl => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                exception.Message,
                "Apri Impostazioni e correggi l'indirizzo Ollama."),
            OllamaErrorKind.Timeout => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                "Ollama non ha risposto in tempo.",
                "Controlla che Ollama sia attivo e aumenta il timeout se la macchina e lenta."),
            OllamaErrorKind.Unreachable => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                "Ollama non e raggiungibile.",
                "Verifica che Ollama sia aperto oppure che l'host LAN sia corretto e accessibile."),
            _ => new OllamaStatusResponse(
                "Offline",
                false,
                baseUrl,
                0,
                exception.Message,
                "Controlla configurazione e modelli in Impostazioni.")
        };
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

    private static async Task CancelDocumentJobIfNeededAsync(
        ImportedDocument document,
        ILocalJobQueue jobs,
        RunningJobCancellationRegistry cancellationRegistry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.CurrentJobId))
        {
            return;
        }

        LocalJob? currentJob = await jobs.GetAsync(document.CurrentJobId, cancellationToken);
        if (currentJob?.Status is not (JobStatus.Pending or JobStatus.Running or JobStatus.Pausing or JobStatus.Paused))
        {
            return;
        }

        await jobs.CancelAsync(document.CurrentJobId, cancellationToken);
        cancellationRegistry.Cancel(document.CurrentJobId);

        // Wait for the job worker to release its SQLite connections before the caller modifies
        // shared tables. Without this, the shared-cache connection used by the running job may
        // still hold an open transaction, causing "SQL logic error" on the subsequent delete.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (cancellationRegistry.IsRunning(document.CurrentJobId))
        {
            if (timeout.Token.IsCancellationRequested)
            {
                throw new TimeoutException($"Il job {document.CurrentJobId} non si e fermato entro 10 secondi. Riprovare.");
            }

            await Task.Delay(80, timeout.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

internal sealed record VectorBackendHealthResponse(
    string BackendName,
    bool StoragePersistent,
    int VectorLimit,
    int TotalVectors,
    bool NearLimit,
    string? Warning);

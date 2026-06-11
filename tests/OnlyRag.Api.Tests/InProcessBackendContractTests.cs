using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task ApiContracts_UnauthorizedRequestsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.GetAsync("/api/documents");

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Non autorizzato",
            "unauthorized");
    }

    [Fact]
    public async Task ApiContracts_NotFoundRequestsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.GetAsync("/api/documents/999999");

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "Documento non trovato",
            "not_found");
    }

    [Fact]
    public async Task ApiContracts_ValidationErrorsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/search",
            new DocumentSearchRequest("", [1], TopK: null));

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Query non valida",
            "search_query_required");
    }

    [Fact]
    public async Task ApiContracts_ConflictErrorsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("contract-conflict-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not-indexed")), "files", "NotIndexed.txt");
        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        DocumentImportResponse? importPayload = await importResponse.Content.ReadFromJsonAsync<DocumentImportResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.NotNull(importPayload);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/translations",
            new CreateTranslationRequest(importPayload.Documents[0].Document.Id, "English", "gemma3:4b"),
            JsonOptions);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "Documento non indicizzato",
            "document_not_indexed");
    }

    [Fact]
    public async Task ApiContracts_DocumentImportResponseUsesSharedDtoShape()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("contract-import-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("contract-document")), "files", "Contract.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement result = json.RootElement.GetProperty("documents")[0];
        JsonElement fileResult = json.RootElement.GetProperty("results")[0];
        JsonElement document = result.GetProperty("document");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("hasFailures").GetBoolean());
        Assert.False(result.GetProperty("deduplicated").GetBoolean());
        Assert.Equal("Contract.txt", fileResult.GetProperty("fileName").GetString());
        Assert.True(fileResult.GetProperty("succeeded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, fileResult.GetProperty("errorCode").ValueKind);
        Assert.Equal("Contract.txt", document.GetProperty("originalFileName").GetString());
        Assert.Equal("Queued", document.GetProperty("status").GetString());
        Assert.True(document.TryGetProperty("currentJobId", out _));
        Assert.False(document.TryGetProperty("CurrentJobId", out _));
    }

    [Fact]
    public async Task ApiContracts_CoreRuntimeDtosUseCamelCaseShapes()
    {
        LocalJobQueueDescriptor queueDescriptor = new("contract-core-dto-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        await queue.CreateAsync(new CreateLocalJobRequest("contract-job", "{}", Priority: 7));

        using HttpResponseMessage jobsResponse = await httpClient.GetAsync("/api/jobs");
        using JsonDocument jobsJson = JsonDocument.Parse(await jobsResponse.Content.ReadAsStringAsync());
        JsonElement job = jobsJson.RootElement[0];

        using HttpResponseMessage settingsResponse = await httpClient.GetAsync("/api/settings/ollama");
        using JsonDocument settingsJson = JsonDocument.Parse(await settingsResponse.Content.ReadAsStringAsync());

        using HttpResponseMessage chatResponse = await httpClient.PostAsJsonAsync(
            "/api/chat",
            new ChatRequest("", "gemma3:4b", UseDocuments: false, SelectedDocumentIds: [], ConversationId: null),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, jobsResponse.StatusCode);
        Assert.True(job.TryGetProperty("progressPercent", out _));
        Assert.True(job.TryGetProperty("currentStep", out _));
        Assert.False(job.TryGetProperty("ProgressPercent", out _));

        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        Assert.True(settingsJson.RootElement.TryGetProperty("ollamaBaseUrl", out _));
        Assert.True(settingsJson.RootElement.TryGetProperty("trustNonLocalEndpoint", out _));
        Assert.False(settingsJson.RootElement.TryGetProperty("OllamaBaseUrl", out _));

        await AssertProblemAsync(
            chatResponse,
            HttpStatusCode.BadRequest,
            "Messaggio non valido",
            "chat_validation_failed");
    }

    [Fact]
    public void ApiContracts_TypeScriptClientResponseShapesMatchCoreDtos()
    {
        string apiSource = ReadTypeScriptApiSource();

        AssertTypeScriptContractMatchesDto<OllamaSettings>(apiSource, "OllamaSettings");
        AssertTypeScriptContractMatchesDto<OllamaModelDetails>(apiSource, "OllamaModelDetails");
        AssertTypeScriptContractMatchesDto<PdfExportSettings>(apiSource, "PdfExportSettings");
        AssertTypeScriptContractMatchesDto<IngestionSettings>(apiSource, "IngestionSettings");
        AssertTypeScriptContractMatchesDto<OcrProcessingSettings>(apiSource, "OcrProcessingSettings");
        AssertTypeScriptContractMatchesDto<PerformanceSettings>(apiSource, "PerformanceSettings");
        AssertTypeScriptContractMatchesDto<OcrSettings>(apiSource, "OcrSettings");
        AssertTypeScriptContractMatchesDto<PdfExportConverterStatusResponse>(apiSource, "PdfExportConverterStatusResponse");
        AssertTypeScriptContractMatchesDto<OllamaStatusResponse>(apiSource, "OllamaStatusResponse");
        AssertTypeScriptContractMatchesDto<OllamaModelSummary>(apiSource, "OllamaModel");
        AssertTypeScriptContractMatchesDto<OllamaModelsResponse>(apiSource, "OllamaModelsResponse");
        AssertTypeScriptContractMatchesDto<OperationMessageResponse>(apiSource, "OperationMessageResponse");
        AssertTypeScriptContractMatchesDto<OllamaInstallStatus>(apiSource, "OllamaInstallStatus");
        AssertTypeScriptContractMatchesDto<DependencyActionResponse>(apiSource, "DependencyActionResponse");
        AssertTypeScriptContractMatchesDto<ImageGenerationSettings>(apiSource, "ImageGenerationSettings");
        AssertTypeScriptContractMatchesDto<ImageGenerationRuntimeStatus>(apiSource, "ImageGenerationRuntimeStatus");
        AssertTypeScriptContractMatchesDto<ImageModelCatalogEntry>(apiSource, "ImageModelCatalogEntry");
        AssertTypeScriptContractMatchesDto<ImageModelCatalogEntryRequest>(apiSource, "ImageModelCatalogEntryRequest");
        AssertTypeScriptContractMatchesDto<ImageModelLocalState>(apiSource, "ImageModelLocalState");
        AssertTypeScriptContractMatchesDto<ImageModelDownloadResponse>(apiSource, "ImageModelDownloadResponse");
        AssertTypeScriptContractMatchesDto<ImageGenerationRequest>(apiSource, "ImageGenerationRequest");
        AssertTypeScriptContractMatchesDto<ImageGenerationResponse>(apiSource, "ImageGenerationResponse");
        AssertTypeScriptContractMatchesDto<GeneratedImage>(apiSource, "GeneratedImage");
        AssertTypeScriptContractMatchesDto<OcrProvisionStatus>(apiSource, "OcrProvisionStatus");
        AssertTypeScriptContractMatchesDto<OcrStartupAnalysisResponse>(apiSource, "OcrStartupAnalysis");
        AssertTypeScriptContractMatchesDto<ImportedDocument>(apiSource, "ImportedDocument");
        AssertTypeScriptContractMatchesDto<DocumentEmbeddingStatusResponse>(apiSource, "DocumentEmbeddingStatus");
        AssertTypeScriptContractMatchesDto<DocumentOcrStatusResponse>(apiSource, "DocumentOcrStatus");
        AssertTypeScriptContractMatchesDto<DocumentSearchResult>(apiSource, "DocumentSearchResult");
        AssertTypeScriptContractMatchesDto<DocumentSearchDocumentStatus>(apiSource, "DocumentSearchDocumentStatus");
        AssertTypeScriptContractMatchesDto<DocumentSearchResponse>(apiSource, "DocumentSearchResponse");
        AssertTypeScriptContractMatchesDto<ChatSource>(apiSource, "ChatSource");
        AssertTypeScriptContractMatchesDto<ChatNotice>(apiSource, "ChatNotice");
        AssertTypeScriptContractMatchesDto<ChatResponse>(apiSource, "ChatResponse");
        AssertTypeScriptContractMatchesDto<TranslationSummaryResponse>(apiSource, "TranslationSummary");
        AssertTypeScriptContractMatchesDto<TranslationUnitResponse>(apiSource, "TranslationUnit");
        AssertTypeScriptContractMatchesDto<TranslationDetailResponse>(apiSource, "TranslationDetail");
        AssertTypeScriptContractMatchesDto<TranslationCompareResponse>(apiSource, "TranslationCompare");
        AssertTypeScriptContractMatchesDto<TranslationExportResponse>(apiSource, "TranslationExport");
        AssertTypeScriptContractMatchesDto<DocumentImportResult>(apiSource, "DocumentImportResult");
        AssertTypeScriptContractMatchesDto<DocumentImportFileResult>(apiSource, "DocumentImportFileResult");
        AssertTypeScriptContractMatchesDto<DocumentImportResponse>(apiSource, "DocumentImportResponse");
        AssertTypeScriptContractMatchesDto<OcrGpuCapabilityResponse>(apiSource, "OcrGpuCapability");
        AssertTypeScriptContractMatchesDto<SystemTelemetryResponse>(apiSource, "SystemTelemetry");
        AssertTypeScriptContractMatchesDto<CpuTelemetryResponse>(apiSource, "CpuTelemetry");
        AssertTypeScriptContractMatchesDto<MemoryTelemetryResponse>(apiSource, "MemoryTelemetry");
        AssertTypeScriptContractMatchesDto<DiskTelemetryResponse>(apiSource, "DiskTelemetry");
        AssertTypeScriptContractMatchesDto<GpuTelemetryResponse>(apiSource, "GpuTelemetry");
        AssertTypeScriptContractMatchesDto<DocumentPageInfo>(apiSource, "DocumentPageInfo");
        AssertTypeScriptContractMatchesDto<DocumentPipelineStatus>(apiSource, "DocumentPipelineStatus");
        AssertTypeScriptContractMatchesDto<PipelinePhaseInfo>(apiSource, "PipelinePhaseInfo");
        AssertTypeScriptContractMatchesDto<DocumentPreviewResponse>(apiSource, "DocumentPreviewResponse");
    }

    [Fact]
    public void ApiContracts_TypeScriptClientEnumLiteralsMatchCoreEnums()
    {
        string apiSource = ReadTypeScriptApiSource();

        AssertTypeScriptUnionMatchesEnum<DocumentStatus>(apiSource, "DocumentStatus");
        AssertTypeScriptUnionMatchesEnum<JobStatus>(apiSource, "JobStatus");
        AssertTypeScriptUnionMatchesEnum<PhaseState>(apiSource, "PhaseState");
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedTitle,
        string expectedCode)
    {
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement root = json.RootElement;

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
    }

    private static void AssertTypeScriptContractMatchesDto<TDto>(string apiSource, string typeScriptTypeName)
    {
        string[] expectedProperties = typeof(TDto)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is not null)
            .Select(GetJsonPropertyName)
            .ToArray();
        string[] actualProperties = ReadTypeScriptObjectPropertyNames(apiSource, typeScriptTypeName).ToArray();

        Assert.Equal(expectedProperties, actualProperties);
    }

    private static void AssertTypeScriptUnionMatchesEnum<TEnum>(string apiSource, string typeScriptTypeName)
        where TEnum : struct, Enum
    {
        string[] expectedValues = Enum.GetNames<TEnum>();
        string[] actualValues = ReadTypeScriptStringUnionValues(apiSource, typeScriptTypeName).ToArray();

        Assert.Equal(expectedValues, actualValues);
    }

    private static string GetJsonPropertyName(PropertyInfo property)
    {
        JsonPropertyNameAttribute? jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (jsonName is not null)
        {
            return jsonName.Name;
        }

        return JsonOptions.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
    }

    private static IReadOnlyList<string> ReadTypeScriptObjectPropertyNames(string apiSource, string typeName)
    {
        string body = ReadTypeScriptObjectBody(apiSource, typeName);
        return body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .Select(ReadTypeScriptPropertyName)
            .Where(propertyName => propertyName is not null)
            .Select(propertyName => propertyName!)
            .ToArray();

        static string? ReadTypeScriptPropertyName(string line)
        {
            int colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            return colonIndex > 0
                ? line[..colonIndex].Trim().TrimEnd('?')
                : null;
        }
    }

    private static IReadOnlyList<string> ReadTypeScriptStringUnionValues(string apiSource, string typeName)
    {
        Match match = Regex.Match(
            apiSource,
            $@"export\s+type\s+{Regex.Escape(typeName)}\s*=\s*(?<body>.*?);",
            RegexOptions.Singleline);
        Assert.True(match.Success, $"TypeScript union {typeName} was not found in the API type sources.");

        return Regex.Matches(match.Groups["body"].Value, "\"(?<value>[^\"]+)\"")
            .Cast<Match>()
            .Select(valueMatch => valueMatch.Groups["value"].Value)
            .ToArray();
    }

    private static string ReadTypeScriptObjectBody(string apiSource, string typeName)
    {
        string marker = $"export type {typeName} = {{";
        int markerIndex = apiSource.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"TypeScript object type {typeName} was not found in the API type sources.");

        int bodyStart = markerIndex + marker.Length;
        int bodyEnd = apiSource.IndexOf("\n};", bodyStart, StringComparison.Ordinal);
        Assert.True(bodyEnd >= 0, $"TypeScript object type {typeName} has no closing marker.");

        return apiSource[bodyStart..bodyEnd];
    }

    private static string ReadTypeScriptApiSource()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string sourceDirectory = Path.Combine(current.FullName, "src", "OnlyRag.Web", "src");
            string apiFile = Path.Combine(sourceDirectory, "api.ts");
            string apiTypesFile = Path.Combine(sourceDirectory, "apiTypes.ts");
            string apiTypesDirectory = Path.Combine(sourceDirectory, "apiTypes");
            if (File.Exists(apiFile) && File.Exists(apiTypesFile) && Directory.Exists(apiTypesDirectory))
            {
                IEnumerable<string> typeFiles = Directory.EnumerateFiles(apiTypesDirectory, "*.ts")
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);
                return string.Join(
                    Environment.NewLine,
                    new[] { apiFile, apiTypesFile }.Concat(typeFiles).Select(File.ReadAllText));
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not find src\\OnlyRag.Web\\src API type sources from the test output directory.");
    }
}

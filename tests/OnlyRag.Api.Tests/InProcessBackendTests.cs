using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private static async Task<ImportedDocument> CreateIndexedDocumentAsync(
        SqliteDocumentRepository documents,
        string root)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-compare-1",
            "compare.txt",
            Path.Combine(root, "compare.txt"),
            "sha-compare",
            "text/plain",
            ".txt",
            64,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));

        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "Pagina uno"),
            [
                new IngestedDocumentChunk(1, 1, 0, "Pagina uno", 2, "hash-compare-a")
            ],
            pageCount: 1);
        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(2, "Pagina due"),
            [
                new IngestedDocumentChunk(2, 2, 1, "Pagina due", 2, "hash-compare-b")
            ],
            pageCount: 2);

        return document;
    }

    private static async Task<SeededTranslation> CreateCompletedTranslationAsync(
        TempBackendDescriptor tempDescriptor,
        string documentName)
    {
        LocalSqliteConnectionFactory connectionFactory = new(tempDescriptor.Descriptor.Store);
        SqliteDocumentRepository documentRepository = new(connectionFactory);
        SqliteTranslationRepository translationRepository = new(connectionFactory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument document = await documentRepository.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            documentName,
            Path.Combine(tempDescriptor.Root, "source.txt"),
            "sha-export",
            "text/plain",
            Path.GetExtension(documentName),
            128,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));

        await documentRepository.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "Primo paragrafo\n\nSecondo paragrafo"),
            [
                new IngestedDocumentChunk(1, 1, 0, "Primo paragrafo", 2, "hash-export-a"),
                new IngestedDocumentChunk(1, 1, 1, "Secondo paragrafo", 2, "hash-export-b")
            ],
            pageCount: 1);
        await documentRepository.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(2, "Riga 1: Cella 1: Nome | Cella 2: Valore"),
            [
                new IngestedDocumentChunk(2, 2, 2, "Riga 1: Cella 1: Nome | Cella 2: Valore", 6, "hash-export-c")
            ],
            pageCount: 2);

        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        IReadOnlyList<StoredTranslationUnit> storedUnits = await translationRepository.ListUnitsAsync(translation.Id);
        string[] translatedTexts = ["Prima traduzione", "Seconda traduzione", "Nome tradotto", "Valore tradotto"];
        for (int index = 0; index < storedUnits.Count; index++)
        {
            await translationRepository.SaveUnitSuccessAsync(
                storedUnits[index].Id,
                translatedTexts[Math.Min(index, translatedTexts.Length - 1)],
                validationWarnings: null);
        }

        await translationRepository.RefreshProgressAsync(translation.Id, "Completed", null);
        return new SeededTranslation((await translationRepository.GetAsync(translation.Id))!, storedUnits);
    }

    private sealed record SeededTranslation(
        StoredTranslation Translation,
        IReadOnlyList<StoredTranslationUnit> Units);

    private static HttpClient CreateAuthenticatedClient(InProcessBackendHandle backend)
    {
        HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };
        httpClient.DefaultRequestHeaders.Add(OnlyRagApiHeaders.SessionTokenHeaderName, backend.SessionToken);
        return httpClient;
    }

    private static LocalDocumentLibraryLimits CreateTestLimits(
        int maxFilesPerImport = 50,
        long maxFileSizeBytes = 100,
        long maxBatchSizeBytes = 100,
        long libraryQuotaBytes = 1_000_000,
        long minimumFreeDiskBytes = 0)
    {
        return new LocalDocumentLibraryLimits
        {
            MaxFilesPerImport = maxFilesPerImport,
            MaxFileSizeBytes = maxFileSizeBytes,
            MaxBatchSizeBytes = maxBatchSizeBytes,
            LibraryQuotaBytes = libraryQuotaBytes,
            MinimumFreeDiskBytes = minimumFreeDiskBytes
        };
    }

    private static IReadOnlyList<string> ListOriginalFiles(TempBackendDescriptor tempDescriptor)
    {
        string originals = tempDescriptor.Descriptor.StoragePaths.DocumentOriginalsDirectory;
        return Directory.Exists(originals)
            ? Directory.EnumerateFiles(originals, "*", SearchOption.AllDirectories).ToArray()
            : [];
    }

    private static string? GetAccessControlAllowOrigin(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? values)
            ? values.Single()
            : null;
    }

    private sealed class FakeProcessLauncher : ILocalProcessLauncher
    {
        public List<ProcessStartInfo> StartedProcesses { get; } = [];

        public string? TryStartErrorMessage { get; init; }

        public bool TryStart(ProcessStartInfo startInfo, out string? errorMessage)
        {
            StartedProcesses.Add(startInfo);
            errorMessage = TryStartErrorMessage;
            return string.IsNullOrWhiteSpace(TryStartErrorMessage);
        }

        public Task<LocalProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? string.Empty
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            StartedProcesses.Add(startInfo);
            return Task.FromResult(new LocalProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
        }
    }

    private sealed class TempBackendDescriptor : IDisposable
    {
        private TempBackendDescriptor(string root, LocalJobQueueDescriptor? jobQueue)
        {
            Root = root;
            AppStoragePaths paths = AppStoragePaths.FromRoot(root);
            Descriptor = new InProcessBackendDescriptor(
                paths,
                new LocalSqliteStoreDescriptor(paths),
                jobQueue ?? LocalJobQueueDescriptor.Default,
                new OllamaEndpointOptions());
        }

        public string Root { get; }

        public InProcessBackendDescriptor Descriptor { get; }

        public static TempBackendDescriptor Create(LocalJobQueueDescriptor? jobQueue = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Api.Tests", Guid.NewGuid().ToString("N"));
            return new TempBackendDescriptor(root, jobQueue);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}



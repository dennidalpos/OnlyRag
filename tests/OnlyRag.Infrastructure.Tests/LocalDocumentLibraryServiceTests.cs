using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Tests;

public sealed class LocalDocumentLibraryServiceTests
{
    [Fact]
    public async Task ImportAsync_ComputesHashPersistsDocumentAndQueuesPlaceholderJob()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalDocumentLibraryService library = await tempStorage.CreateLibraryAsync();

        byte[] bytes = Encoding.UTF8.GetBytes("onlyrag-document");
        await using MemoryStream stream = new(bytes);

        DocumentImportResult result = await library.ImportAsync(stream, "Manuale.pdf");
        IReadOnlyList<LocalJob> jobs = await tempStorage.CreateQueue().ListAsync();

        Assert.False(result.Deduplicated);
        Assert.Equal(DocumentStatus.Queued, result.Document.Status);
        Assert.Equal("manuale.pdf", result.Document.OriginalFileName.ToLowerInvariant());
        Assert.Equal("application/pdf", result.Document.MimeType);
        Assert.Equal(".pdf", result.Document.FileExtension);
        Assert.Equal(bytes.Length, result.Document.FileSizeBytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            result.Document.Sha256);
        Assert.True(File.Exists(result.Document.OriginalPath));
        Assert.StartsWith(
            Path.GetFullPath(tempStorage.Paths.DocumentOriginalsDirectory),
            Path.GetFullPath(result.Document.OriginalPath),
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(jobs);
        Assert.Equal(LocalDocumentLibraryService.DocumentIngestionJobType, jobs[0].Type);

        DocumentIngestionJobPayload? payload = JsonSerializer.Deserialize<DocumentIngestionJobPayload>(jobs[0].PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(result.Document.Id, payload.DocumentId);
    }

    [Fact]
    public async Task ImportAsync_DeduplicatesByHashWithoutWritingSecondCopy()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalDocumentLibraryService library = await tempStorage.CreateLibraryAsync();

        byte[] bytes = Encoding.UTF8.GetBytes("duplicate-content");

        await using MemoryStream firstStream = new(bytes, writable: false);
        DocumentImportResult first = await library.ImportAsync(firstStream, "Scan1.png");

        await using MemoryStream secondStream = new(bytes, writable: false);
        DocumentImportResult second = await library.ImportAsync(secondStream, "Scan2.png");

        IReadOnlyList<LocalJob> jobs = await tempStorage.CreateQueue().ListAsync();
        string[] storedFiles = Directory.GetFiles(tempStorage.Paths.DocumentOriginalsDirectory);

        Assert.False(first.Deduplicated);
        Assert.True(second.Deduplicated);
        Assert.Equal(first.Document.Id, second.Document.Id);
        Assert.Single(storedFiles);
        Assert.Single(jobs);
    }

    [Fact]
    public async Task ImportAsync_DeduplicatesConcurrentImportsByHash()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalDocumentLibraryService library = await tempStorage.CreateLibraryAsync();
        byte[] bytes = Encoding.UTF8.GetBytes("concurrent-duplicate-content");

        DocumentImportResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 4)
                .Select(index => ImportDuplicateAsync(index)));

        IReadOnlyList<ImportedDocument> documents = await library.ListAsync();
        IReadOnlyList<LocalJob> jobs = await tempStorage.CreateQueue().ListAsync();
        string[] storedFiles = Directory.GetFiles(tempStorage.Paths.DocumentOriginalsDirectory);

        Assert.Single(documents);
        Assert.Single(results.Select(result => result.Document.Id).Distinct());
        Assert.Equal(1, results.Count(result => !result.Deduplicated));
        Assert.Equal(3, results.Count(result => result.Deduplicated));
        Assert.Single(storedFiles);
        Assert.Single(jobs);

        async Task<DocumentImportResult> ImportDuplicateAsync(int index)
        {
            await using MemoryStream stream = new(bytes, writable: false);
            return await library.ImportAsync(stream, $"Concurrent-{index}.pdf");
        }
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteOriginalStillReferencedByAnotherDocument()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalDocumentLibraryService library = await tempStorage.CreateLibraryAsync();
        SqliteDocumentRepository documents = tempStorage.CreateDocumentRepository();
        string sharedPath = Path.Combine(tempStorage.Paths.DocumentOriginalsDirectory, "shared.txt");
        Directory.CreateDirectory(tempStorage.Paths.DocumentOriginalsDirectory);
        await File.WriteAllTextAsync(sharedPath, "shared content");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument first = await documents.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            "first.txt",
            sharedPath,
            "shared-sha-a",
            "text/plain",
            ".txt",
            14,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));
        ImportedDocument second = await documents.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            "second.txt",
            sharedPath,
            "shared-sha-b",
            "text/plain",
            ".txt",
            14,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));

        ImportedDocument? deleted = await library.DeleteAsync(first.Id);
        ImportedDocument? queued = await library.QueueForIndexingAsync(second.Id);

        Assert.NotNull(deleted);
        Assert.True(File.Exists(sharedPath));
        Assert.NotNull(queued);
        Assert.Equal(DocumentStatus.Queued, queued.Status);
    }

    [Fact]
    public async Task ImportAsync_RejectsUnsupportedExtension()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalDocumentLibraryService library = await tempStorage.CreateLibraryAsync();

        byte[] bytes = Encoding.UTF8.GetBytes("{\"key\": \"value\"}");
        await using MemoryStream stream = new(bytes);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            library.ImportAsync(stream, "export.json"));
    }

    [Fact]
    public async Task ImportAsync_RejectsRtfExtension()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalDocumentLibraryService library = await tempStorage.CreateLibraryAsync();

        byte[] bytes = Encoding.UTF8.GetBytes(@"{\rtf1\ansi Hello, RTF!}");
        await using MemoryStream stream = new(bytes);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            library.ImportAsync(stream, "document.rtf"));
    }

    [Fact]
    public void ResolveWithinRoot_NormalizesTraversalBackIntoDocumentRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Path.Tests", Guid.NewGuid().ToString("N"));
        string resolved = SafeDocumentPath.ResolveWithinRoot(root, @"..\..\escape.pdf");
        string rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Assert.StartsWith(rootWithSeparator, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("escape.pdf", Path.GetFileName(resolved));
    }

    [Fact]
    public void NormalizeFileName_RejectsInvalidWindowsCharacters()
    {
        Assert.Throws<ArgumentException>(() => SafeDocumentPath.NormalizeFileName("bad<name>.pdf"));
    }

    private sealed class TempStorage : IDisposable
    {
        private TempStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public static TempStorage Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Documents.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public async Task<LocalDocumentLibraryService> CreateLibraryAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteMigrator migrator = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, migrator);
            await storage.InitializeAsync();
            return new LocalDocumentLibraryService(
                Descriptor,
                new SqliteDocumentRepository(connectionFactory),
                CreateQueue(),
                new LocalDocumentStorageGuard(Descriptor, LocalDocumentLibraryLimits.Default));
        }

        public SqliteDocumentRepository CreateDocumentRepository()
        {
            return new SqliteDocumentRepository(CreateConnectionFactory());
        }

        public SqliteLocalJobQueue CreateQueue()
        {
            return new SqliteLocalJobQueue(CreateConnectionFactory(), LocalJobQueueDescriptor.Default);
        }

        private LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(Descriptor);
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

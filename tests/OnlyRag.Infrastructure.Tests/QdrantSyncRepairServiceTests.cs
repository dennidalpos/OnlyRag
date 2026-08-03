using NSubstitute;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class QdrantSyncRepairServiceTests
{
    [Fact]
    public async Task AuditAndRepairAsync_IdentifiesMissingVectorsAndEnqueuesJobs()
    {
        var docRepo = Substitute.For<IDocumentRepository>();
        var embRepo = Substitute.For<IEmbeddingRepository>();
        var vectorStore = Substitute.For<IQdrantVectorStore>();
        var jobQueue = Substitute.For<ILocalJobQueue>();

        var doc1 = new ImportedDocument(
            Id: 1,
            DocumentUid: "doc-1",
            OriginalFileName: "doc1.txt",
            OriginalPath: "C:\\tmp\\doc1.txt",
            Sha256: "abc",
            MimeType: "text/plain",
            FileExtension: ".txt",
            FileSizeBytes: 100,
            Status: DocumentStatus.Indexed,
            PageCount: 1,
            ChunkCount: 10,
            CurrentJobId: null,
            LastError: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        docRepo.ListAsync(Arg.Any<CancellationToken>()).Returns([doc1]);

        embRepo.GetDocumentEmbeddingStatusAsync(1, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentEmbeddingStatusSnapshot(1, "nomic-embed-text", 10, 6, DateTimeOffset.UtcNow)); // 4 missing

        var service = new QdrantSyncRepairService(docRepo, embRepo, vectorStore, jobQueue);
        QdrantSyncReport report = await service.AuditAndRepairAsync();

        Assert.Equal(1, report.TotalDocumentsChecked);
        Assert.Equal(10, report.TotalChunksInStorage);
        Assert.Equal(4, report.MissingVectorCount);
        Assert.Equal(1, report.EnqueuedRepairJobs);
        await jobQueue.Received(1).CreateAsync(Arg.Is<CreateLocalJobRequest>(req => req.Type == "document.embedding" && req.PayloadJson.Contains("\"documentId\": 1")), Arg.Any<CancellationToken>());
    }
}

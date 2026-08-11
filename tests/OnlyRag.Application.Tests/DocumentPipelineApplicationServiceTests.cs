using NSubstitute;
using OnlyRag.Application.Documents;
using OnlyRag.Application.Jobs;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Application.Tests;

public sealed class DocumentPipelineApplicationServiceTests
{
    [Fact]
    public async Task QueueOcrAsync_CancelsActiveJobBeforeQueueingNewOne()
    {
        IDocumentLibraryService documents = Substitute.For<IDocumentLibraryService>();
        ILocalJobQueue queue = Substitute.For<ILocalJobQueue>();
        RunningJobCancellationRegistry registry = new();
        JobApplicationService jobs = new(queue, registry);

        ImportedDocument document = CreateDocument(42, "job-1");
        LocalJob currentJob = CreateJob("job-1", JobStatus.Running);
        LocalJob queuedJob = CreateJob("job-2", JobStatus.Pending);

        documents.GetAsync(42, Arg.Any<CancellationToken>()).Returns(document);
        queue.GetAsync("job-1", Arg.Any<CancellationToken>()).Returns(currentJob);
        queue.CancelAsync("job-1", Arg.Any<CancellationToken>()).Returns(currentJob);
        queue.CreateAsync(Arg.Any<CreateLocalJobRequest>(), Arg.Any<CancellationToken>()).Returns(queuedJob);
        documents.SetStatusAsync(42, DocumentStatus.Queued, queuedJob.Id, null, Arg.Any<CancellationToken>()).Returns(document);

        ImportedDocument? result = await new DocumentPipelineApplicationService(documents, jobs)
            .QueueOcrAsync(42, forceOcr: true, ocrLanguage: "it", CancellationToken.None);

        Assert.Same(document, result);
        await queue.Received(1).CancelAsync("job-1", Arg.Any<CancellationToken>());
        await documents.Received(1).SetStatusAsync(42, DocumentStatus.Queued, queuedJob.Id, null, Arg.Any<CancellationToken>());
    }

    private static ImportedDocument CreateDocument(long id, string? currentJobId) =>
        new(
            id,
            $"doc-{id}",
            $"file-{id}.pdf",
            $"/tmp/file-{id}.pdf",
            "sha256",
            "application/pdf",
            ".pdf",
            1024,
            DocumentStatus.Imported,
            3,
            0,
            currentJobId,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static LocalJob CreateJob(string id, JobStatus status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new LocalJob(
            id,
            "test",
            status,
            0,
            0,
            string.Empty,
            "{}",
            "{}",
            null,
            0,
            3,
            null,
            now,
            now);
    }
}

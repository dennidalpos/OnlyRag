using NSubstitute;
using OnlyRag.Application.Jobs;
using OnlyRag.Application.Translations;
using OnlyRag.Core;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Application.Tests;

public sealed class TranslationApplicationServiceTests
{
    [Fact]
    public async Task StartAsync_CreatesTranslationAndQueuesJob()
    {
        IDocumentLibraryService documents = Substitute.For<IDocumentLibraryService>();
        ITranslationRepository translations = Substitute.For<ITranslationRepository>();
        ILocalJobQueue queue = Substitute.For<ILocalJobQueue>();
        RunningJobCancellationRegistry registry = new();
        JobApplicationService jobs = new(queue, registry);

        ImportedDocument document = CreateDocument(10);
        StoredTranslation translation = CreateTranslation(99, document.Id);
        LocalJob queuedJob = CreateJob("job-translation", JobStatus.Pending);

        documents.GetAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        translations.BuildSourceUnitsAsync(document.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<TranslationSourceUnit>());
        translations.CreateAsync(document.Id, "it", "model-x", null, Arg.Any<IReadOnlyList<TranslationSourceUnit>>(), Arg.Any<CancellationToken>()).Returns(translation);
        queue.CreateAsync(Arg.Any<CreateLocalJobRequest>(), Arg.Any<CancellationToken>()).Returns(queuedJob);
        translations.UpdateTranslationJobAsync(translation.Id, queuedJob.Id, "Queued", null, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        StoredTranslation result = await new TranslationApplicationService(documents, translations, jobs)
            .StartAsync(document.Id, "it", "model-x", new Dictionary<string, string> { ["term"] = "glossary" }, CancellationToken.None);

        Assert.Same(translation, result);
        await translations.Received(1).UpdateTranslationJobAsync(translation.Id, queuedJob.Id, "Queued", null, Arg.Any<CancellationToken>());
    }

    private static ImportedDocument CreateDocument(long id) =>
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
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static StoredTranslation CreateTranslation(long id, long documentId) =>
        new(
            id,
            documentId,
            "document.pdf",
            "it",
            "en",
            "model-x",
            "Queued",
            null,
            0,
            0,
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

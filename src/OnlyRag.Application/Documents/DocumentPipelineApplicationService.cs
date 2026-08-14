using System.Text.Json;
using OnlyRag.Application.Jobs;
using OnlyRag.Core;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Application.Documents;

public sealed class DocumentPipelineApplicationService
{
    private readonly IDocumentLibraryService documents;
    private readonly JobApplicationService jobs;

    public DocumentPipelineApplicationService(IDocumentLibraryService documents, JobApplicationService jobs)
    {
        this.documents = documents;
        this.jobs = jobs;
    }

    public async Task<ImportedDocument?> QueueReindexAsync(
        long documentId,
        string? ocrLanguage,
        CancellationToken cancellationToken)
    {
        ImportedDocument? existing = await documents.GetAsync(documentId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        await jobs.CancelAndWaitAsync(existing.CurrentJobId, cancellationToken);
        return await documents.QueueForIndexingAsync(documentId, ocrLanguage, cancellationToken);
    }

    public async Task<ImportedDocument?> QueueOcrAsync(
        long documentId,
        bool forceOcr,
        string? ocrLanguage,
        CancellationToken cancellationToken)
    {
        ImportedDocument? existing = await documents.GetAsync(documentId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        await jobs.CancelAndWaitAsync(existing.CurrentJobId, cancellationToken);

        string payloadJson = JsonSerializer.Serialize(new DocumentIngestionJobPayload(
            existing.Id,
            existing.DocumentUid,
            existing.OriginalFileName,
            existing.Sha256 ?? string.Empty,
            ForceOcr: forceOcr,
            OcrLanguage: ocrLanguage));

        LocalJob job = await jobs.CreateAsync(
            new CreateLocalJobRequest(
                DocumentJobTypes.DocumentIngestionJobType,
                payloadJson,
                Priority: 30),
            cancellationToken);

        return await documents.SetStatusAsync(
            documentId,
            DocumentStatus.Queued,
            job.Id,
            lastError: null,
            cancellationToken);
    }
}

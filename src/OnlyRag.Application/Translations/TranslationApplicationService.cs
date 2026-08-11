using System.Text.Json;
using OnlyRag.Application.Jobs;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Application.Translations;

public sealed class TranslationApplicationService
{
    private const string DocumentTranslationJobType = "document-translation";

    private readonly IDocumentLibraryService documents;
    private readonly ITranslationRepository translations;
    private readonly JobApplicationService jobs;

    public TranslationApplicationService(
        IDocumentLibraryService documents,
        ITranslationRepository translations,
        JobApplicationService jobs)
    {
        this.documents = documents;
        this.translations = translations;
        this.jobs = jobs;
    }

    public async Task<StoredTranslation> StartAsync(
        long documentId,
        string targetLanguage,
        string model,
        IReadOnlyDictionary<string, string>? customGlossary,
        CancellationToken cancellationToken)
    {
        ImportedDocument? document = await documents.GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException("Document not found.");
        }

        if (document.PageCount == 0)
        {
            throw new InvalidOperationException("Ingest the document first: translation relies on indexed text units.");
        }

        IReadOnlyList<TranslationSourceUnit> units = await translations.BuildSourceUnitsAsync(documentId, cancellationToken);
        StoredTranslation translation = await translations.CreateAsync(
            documentId,
            targetLanguage,
            model,
            jobId: null,
            units,
            cancellationToken);

        string payloadJson = JsonSerializer.Serialize(new DocumentTranslationJobPayload(
            translation.Id,
            documentId,
            targetLanguage,
            model,
            customGlossary));

        LocalJob job = await jobs.CreateAsync(
            new CreateLocalJobRequest(
                DocumentTranslationJobType,
                payloadJson,
                Priority: 20),
            cancellationToken);

        await translations.UpdateTranslationJobAsync(translation.Id, job.Id, "Queued", null, cancellationToken);
        return translation;
    }
}

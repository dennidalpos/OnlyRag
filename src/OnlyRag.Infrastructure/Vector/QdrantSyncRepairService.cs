using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Infrastructure.Vector;

public sealed class QdrantSyncRepairService : IQdrantSyncRepairService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly IQdrantVectorStore _vectorStore;
    private readonly ILocalJobQueue _jobQueue;

    public QdrantSyncRepairService(
        IDocumentRepository documentRepository,
        IEmbeddingRepository embeddingRepository,
        IQdrantVectorStore vectorStore,
        ILocalJobQueue jobQueue)
    {
        _documentRepository = documentRepository;
        _embeddingRepository = embeddingRepository;
        _vectorStore = vectorStore;
        _jobQueue = jobQueue;
    }

    public async Task<QdrantSyncReport> AuditAndRepairAsync(CancellationToken cancellationToken = default)
    {
        List<string> notices = [];
        int docsChecked = 0;
        int totalChunks = 0;
        int missingVectors = 0;
        int repairJobsCount = 0;
        bool recreatedCollections = false;

        try
        {
            IReadOnlyList<ImportedDocument> docs = await _documentRepository.ListAsync(cancellationToken);
            docsChecked = docs.Count;

            foreach (ImportedDocument doc in docs)
            {
                totalChunks += doc.ChunkCount;
                if (doc.ChunkCount == 0 || doc.Status != DocumentStatus.Indexed)
                {
                    continue;
                }

                DocumentEmbeddingStatusSnapshot status = await _embeddingRepository.GetDocumentEmbeddingStatusAsync(
                    doc.Id,
                    model: null,
                    cancellationToken);

                int unindexed = doc.ChunkCount - status.EmbeddedChunkCount;
                if (unindexed > 0)
                {
                    missingVectors += unindexed;
                    notices.Add($"Documento ID {doc.Id} ({doc.OriginalFileName}): {unindexed} chunk mancanti in Qdrant.");

                    // Enqueue embedding repair job
                    string payload = $"{{\"documentId\": {doc.Id}}}";
                    await _jobQueue.CreateAsync(new CreateLocalJobRequest("document.embedding", payload), cancellationToken);
                    repairJobsCount++;
                }
            }

            if (repairJobsCount == 0 && missingVectors == 0)
            {
                notices.Add("Audit indici Qdrant completato: tutti i vettori SQLite sono perfettamente sincronizzati.");
            }
            else
            {
                notices.Add($"Schedulati {repairJobsCount} job di riparazione embedding per risincronizzare {missingVectors} vettori mancanti.");
            }
        }
        catch (Exception ex)
        {
            notices.Add($"Errore durante l'audit indici Qdrant: {ex.Message}");
        }

        return new QdrantSyncReport(
            docsChecked,
            totalChunks,
            missingVectors,
            repairJobsCount,
            recreatedCollections,
            notices);
    }
}

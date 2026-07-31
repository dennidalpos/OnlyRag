using System.Text.Json.Serialization;

namespace OnlyRag.Core;

public enum DocumentStatus
{
    Imported,
    Queued,
    Processing,
    Indexed,
    RequiresEmbeddingRebuild,
    RequiresAdditionalComponent,
    Failed
}

public sealed record ImportedDocument(
    long Id,
    string DocumentUid,
    string OriginalFileName,
    string OriginalPath,
    string? Sha256,
    string? MimeType,
    string? FileExtension,
    long FileSizeBytes,
    DocumentStatus Status,
    int PageCount,
    int ChunkCount,
    string? CurrentJobId,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DocumentPageInfo(
    int PageNumber,
    string? TextContent,
    string? OcrStatus,
    string? OcrEngine,
    double? OcrConfidence,
    string? OcrError);

public sealed record DocumentImportResult(
    ImportedDocument Document,
    bool Deduplicated,
    string Message);

public sealed record DocumentImportFileResult(
    string FileName,
    ImportedDocument? Document,
    bool Deduplicated,
    bool Succeeded,
    string Message,
    string? ErrorCode)
{
    public static DocumentImportFileResult Imported(string fileName, DocumentImportResult result)
    {
        return new DocumentImportFileResult(
            fileName,
            result.Document,
            result.Deduplicated,
            Succeeded: true,
            result.Message,
            ErrorCode: null);
    }

    public static DocumentImportFileResult Failed(string fileName, string message, string errorCode)
    {
        return new DocumentImportFileResult(
            fileName,
            Document: null,
            Deduplicated: false,
            Succeeded: false,
            message,
            errorCode);
    }
}

public sealed record DocumentImportResponse
{
    [JsonConstructor]
    public DocumentImportResponse(
        IReadOnlyList<DocumentImportResult> documents,
        IReadOnlyList<DocumentImportFileResult> results,
        bool hasFailures)
    {
        Documents = documents;
        Results = results;
        HasFailures = hasFailures;
    }

    public DocumentImportResponse(IReadOnlyList<DocumentImportResult> documents)
        : this(
            documents,
            documents.Select(document => DocumentImportFileResult.Imported(
                    document.Document.OriginalFileName,
                    document))
                .ToArray(),
            hasFailures: false)
    {
    }

    public IReadOnlyList<DocumentImportResult> Documents { get; init; }

    public IReadOnlyList<DocumentImportFileResult> Results { get; init; }

    public bool HasFailures { get; init; }
}

public sealed record DocumentPreviewResponse(
    long DocumentId,
    string OriginalFileName,
    string? MimeType,
    string? FileExtension,
    long FileSizeBytes,
    int PageCount,
    int ChunkCount,
    string Status,
    int PageStart,
    int PageSize,
    int ReturnedPageCount,
    IReadOnlyList<DocumentPageInfo> Pages);

public sealed record DocumentPreAnalysis(
    string FileName,
    string FileExtension,
    string MimeType,
    long FileSizeBytes,
    bool IsOcrCandidate,
    int? EstimatedPageCount);

public sealed record DocumentSearchRequest(
    string Query,
    IReadOnlyList<long> DocumentIds,
    int? TopK);

public sealed record DocumentSearchResponse(
    IReadOnlyList<DocumentSearchResult> Results,
    IReadOnlyList<DocumentSearchDocumentStatus> Documents,
    string KeywordBackend,
    string VectorBackend,
    int MaxContextCharacters)
{
    public IReadOnlyList<RetrievalNotice> Notices { get; init; } = [];
}

public sealed record RetrievalNotice(
    string Code,
    string Message);

public sealed record DocumentSearchResult(
    long DocumentId,
    string DocumentName,
    int? PageStart,
    int? PageEnd,
    long ChunkId,
    string Snippet,
    double Score,
    double? ReRankScore = null,
    string? ParentContent = null,
    string? QueryVariant = null,
    string? SectionHeading = null,
    string ChunkLevel = "Child");

public sealed record DocumentSearchDocumentStatus(
    long DocumentId,
    string DocumentName,
    DocumentStatus Status,
    bool IsIndexed,
    string EmbeddingState,
    int ChunkCount,
    int EmbeddedChunkCount);

public enum PipelinePhase
{
    Import,
    Analysis,
    Ocr,
    TextExtraction,
    Chunking,
    Embedding,
    Ready
}

public enum PhaseState
{
    Todo,
    InProgress,
    Completed,
    Skipped,
    Failed
}

public sealed record PipelinePhaseInfo(
    PhaseState State,
    string? Error,
    DateTimeOffset? CompletedAtUtc);

public sealed record DocumentPipelineStatus(
    long DocumentId,
    string OcrPolicy,
    PipelinePhaseInfo Import,
    PipelinePhaseInfo Analysis,
    PipelinePhaseInfo Ocr,
    PipelinePhaseInfo TextExtraction,
    PipelinePhaseInfo Chunking,
    PipelinePhaseInfo Embedding,
    PhaseState OverallState,
    string? ActiveJobId,
    string? ActiveJobType);

public sealed record DocumentIngestionJobPayload(
    long DocumentId,
    string DocumentUid,
    string OriginalFileName,
    string Sha256,
    bool ForceOcr = false,
    string? OcrLanguage = null);

public sealed record DocumentEmbeddingJobPayload(
    long DocumentId,
    string Model);

public sealed record DocumentOcrStatusResponse(
    long DocumentId,
    string State,
    int PageCount,
    int OcrPageCount,
    int CurrentPage,
    int TotalPages,
    double? AverageConfidence,
    string? CurrentJobId,
    string? CurrentStep,
    string? EngineName,
    string? LastError);

public sealed record DocumentEmbeddingStatusResponse(
    long DocumentId,
    string State,
    string? Model,
    int ChunkCount,
    int EmbeddedChunkCount,
    int ProgressPercent,
    string? CurrentJobId,
    string? CurrentStep,
    string VectorSearchBackend,
    DateTimeOffset? LastEmbeddedAtUtc);

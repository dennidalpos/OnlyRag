using System.Text.Json.Serialization;

namespace OnlyRag.Core;

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

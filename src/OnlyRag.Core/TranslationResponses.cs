namespace OnlyRag.Core;

public sealed record TranslationSummaryResponse(
    long Id,
    long DocumentId,
    string DocumentName,
    string SourceLanguage,
    string TargetLanguage,
    string Model,
    string Status,
    string? JobId,
    int UnitCount,
    int CompletedUnitCount,
    int ProgressPercent,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TranslationUnitResponse(
    long Id,
    long TranslationId,
    int UnitIndex,
    string UnitKind,
    int? PageNumber,
    string SourceText,
    string? MachineTranslatedText,
    string? TranslatedText,
    string Status,
    bool ManuallyEdited,
    string? ValidationWarnings,
    string? Error,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TranslationDetailResponse(
    TranslationSummaryResponse Translation,
    IReadOnlyList<TranslationUnitResponse> Units);

public sealed record TranslationCompareResponse(
    TranslationSummaryResponse Translation,
    int CurrentPage,
    int PagePosition,
    int PageCount,
    int? PreviousPage,
    int? NextPage,
    IReadOnlyList<TranslationUnitResponse> Units);

public sealed record TranslationExportRequest(string Format);

public sealed record TranslationExportResponse(
    long TranslationId,
    string Format,
    string OutputPath,
    string Status);

using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed record TranslationSourceUnit(
    int UnitIndex,
    string UnitKind,
    long? DocumentPageId,
    int? PageNumber,
    string SourceText,
    string SourceHash,
    string LayoutMetadataJson);

public sealed record StoredTranslation(
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
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public TranslationSummaryResponse ToResponse()
    {
        int progressPercent = UnitCount == 0
            ? 0
            : (int)Math.Round(CompletedUnitCount * 100d / UnitCount);
        return new TranslationSummaryResponse(
            Id,
            DocumentId,
            DocumentName,
            SourceLanguage,
            TargetLanguage,
            Model,
            Status,
            JobId,
            UnitCount,
            CompletedUnitCount,
            Math.Clamp(progressPercent, 0, 100),
            LastError,
            CreatedAtUtc,
            UpdatedAtUtc);
    }
}

public sealed record StoredTranslationUnit(
    long Id,
    long TranslationId,
    int UnitIndex,
    string UnitKind,
    long? DocumentPageId,
    int? PageNumber,
    string SourceText,
    string SourceHash,
    string LayoutMetadataJson,
    string? MachineTranslatedText,
    string? TranslatedText,
    string Status,
    bool ManuallyEdited,
    string? ValidationWarnings,
    string? Error,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public TranslationUnitResponse ToResponse()
    {
        return new TranslationUnitResponse(
            Id,
            TranslationId,
            UnitIndex,
            UnitKind,
            PageNumber,
            SourceText,
            MachineTranslatedText,
            TranslatedText,
            Status,
            ManuallyEdited,
            ValidationWarnings,
            Error,
            AttemptCount,
            CreatedAtUtc,
            UpdatedAtUtc);
    }
}

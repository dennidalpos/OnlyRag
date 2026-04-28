namespace OnlyRag.Infrastructure.Storage;

public interface ITranslationRepository
{
    Task<IReadOnlyList<TranslationSourceUnit>> BuildSourceUnitsAsync(
        long documentId,
        CancellationToken cancellationToken = default);

    Task<StoredTranslation> CreateAsync(
        long documentId,
        string targetLanguage,
        string model,
        string? jobId,
        IReadOnlyList<TranslationSourceUnit> units,
        CancellationToken cancellationToken = default);

    Task<StoredTranslation?> GetAsync(long translationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredTranslation>> ListByDocumentAsync(
        long documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsAsync(
        long translationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsPreviewAsync(
        long translationId,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> ListUnitPagesAsync(
        long translationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsByPageAsync(
        long translationId,
        int pageNumber,
        CancellationToken cancellationToken = default);

    Task<StoredTranslationUnit?> GetUnitAsync(
        long translationId,
        long unitId,
        CancellationToken cancellationToken = default);

    Task<StoredTranslationUnit?> GetNextPendingUnitAsync(
        long translationId,
        int afterUnitIndex,
        CancellationToken cancellationToken = default);

    Task SaveUnitSuccessAsync(
        long unitId,
        string translatedText,
        string? validationWarnings,
        CancellationToken cancellationToken = default);

    Task SaveUnitFailureAsync(
        long unitId,
        string error,
        CancellationToken cancellationToken = default);

    Task<StoredTranslationUnit?> UpdateUnitTextAsync(
        long translationId,
        long unitId,
        string translatedText,
        CancellationToken cancellationToken = default);

    Task UpdateTranslationJobAsync(
        long translationId,
        string? jobId,
        string status,
        string? lastError,
        CancellationToken cancellationToken = default);

    Task RefreshProgressAsync(
        long translationId,
        string status,
        string? lastError,
        CancellationToken cancellationToken = default);
}

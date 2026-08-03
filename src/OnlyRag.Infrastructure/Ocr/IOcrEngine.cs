namespace OnlyRag.Infrastructure.Ocr;

public interface IOcrEngine
{
    string EngineName { get; }

    string EngineVersion { get; }

    string PreprocessVersion { get; }

    Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<OcrEngineAvailability> CheckAvailabilityAsync(
        string device,
        CancellationToken cancellationToken = default);

    Task<OcrPagePreparation> PreparePageAsync(
        OcrPagePreparationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OcrPagePreparation>> PreparePageBatchAsync(
        IReadOnlyList<OcrPagePreparationRequest> requests,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default);

    Task<OcrPageResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OcrPageResult>> RecognizeBatchAsync(
        IReadOnlyList<OcrRecognitionRequest> requests,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default);
}

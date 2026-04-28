namespace OnlyRag.Infrastructure.Ocr;

public interface IOcrEngine
{
    string EngineName { get; }

    string EngineVersion { get; }

    string PreprocessVersion { get; }

    Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<OcrPagePreparation> PreparePageAsync(
        OcrPagePreparationRequest request,
        CancellationToken cancellationToken = default);

    Task<OcrPageResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default);
}

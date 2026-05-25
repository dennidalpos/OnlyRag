namespace OnlyRag.Infrastructure.Ocr;

public sealed class UnavailableOcrEngine : IOcrEngine
{
    public string EngineName => "PaddleOCR";

    public string EngineVersion => "not-configured";

    public string PreprocessVersion => "onlyrag-preprocess-v2";

    public Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OcrEngineAvailability(
            false,
            EngineName,
            EngineVersion,
            "Runtime OCR non installato. Apri Impostazioni > Diagnostica e usa Installa OCR, oppure imposta ONLYRAG_OCR_PYTHON."));
    }

    public Task<OcrEngineAvailability> CheckAvailabilityAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        return CheckAvailabilityAsync(cancellationToken);
    }

    public Task<OcrPagePreparation> PreparePageAsync(
        OcrPagePreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new OcrEngineUnavailableException("Runtime OCR non installato.");
    }

    public Task<OcrPageResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new OcrEngineUnavailableException("Runtime OCR non installato.");
    }
}

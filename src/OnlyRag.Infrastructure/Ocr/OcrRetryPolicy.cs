namespace OnlyRag.Infrastructure.Ocr;

public sealed class OcrRetryPolicy
{
    public async Task<OcrPageResult> ExecuteAsync(
        Func<CancellationToken, Task<OcrPageResult>> recognizeAsync,
        OcrPipelineOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recognizeAsync);
        ArgumentNullException.ThrowIfNull(options);

        Exception? lastError = null;
        OcrPageResult? lowConfidenceResult = null;
        int attempts = options.MaxRetries + 1;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using CancellationTokenSource pageTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            pageTimeout.CancelAfter(options.PageTimeout);

            try
            {
                OcrPageResult result = await recognizeAsync(pageTimeout.Token);
                if (result.AverageConfidence is null || result.AverageConfidence >= options.LowConfidenceThreshold)
                {
                    return result;
                }

                lowConfidenceResult = result;
                lastError = new InvalidOperationException(
                    $"Confidence OCR bassa ({result.AverageConfidence:0.000}).");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException(
                    $"Timeout OCR pagina dopo {options.PageTimeout.TotalSeconds:0} secondi.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
            {
                lastError = ex;
            }
        }

        if (lowConfidenceResult is not null)
        {
            return lowConfidenceResult;
        }

        string message = lastError is null
            ? "OCR pagina fallito dopo i retry configurati."
            : $"OCR pagina fallito dopo i retry configurati: {lastError.Message}";
        throw new InvalidOperationException(message, lastError);
    }
}

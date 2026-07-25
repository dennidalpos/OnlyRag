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
        int maxAttempts = Math.Max(1, options.MaxRetries + 1);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                OcrPageResult result = await recognizeAsync(cancellationToken);
                if (result.AverageConfidence is null || result.AverageConfidence >= options.LowConfidenceThreshold)
                {
                    return result;
                }

                lowConfidenceResult = result;
                lastError = new InvalidOperationException($"Confidence OCR bassa ({result.AverageConfidence:0.000}).");
            }
            catch (OperationCanceledException)
            {
                throw;
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
            ? "OCR pagina fallito."
            : $"OCR pagina fallito: {lastError.Message}";
        throw new InvalidOperationException(message, lastError);
    }
}


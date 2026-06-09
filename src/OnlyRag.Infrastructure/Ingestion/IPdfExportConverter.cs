namespace OnlyRag.Infrastructure.Ingestion;

public interface IPdfExportConverter
{
    Task<PdfExportConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<PdfExportConversionResult> ConvertToPdfAsync(
        PdfExportConversionRequest request,
        CancellationToken cancellationToken = default);
}

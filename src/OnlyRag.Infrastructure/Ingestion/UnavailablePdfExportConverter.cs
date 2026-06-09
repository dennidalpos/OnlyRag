namespace OnlyRag.Infrastructure.Ingestion;

public sealed class UnavailablePdfExportConverter : IPdfExportConverter
{
    private const string UnavailableMessage = "Installa LibreOffice per Windows oppure configura il path di soffice.exe in Impostazioni per abilitare l'export PDF.";

    public Task<PdfExportConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PdfExportConverterAvailability(
            false,
            null,
            "Convertitore PDF non disponibile.",
            UnavailableMessage));
    }

    public Task<PdfExportConversionResult> ConvertToPdfAsync(
        PdfExportConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new PdfExportConversionUnavailableException(UnavailableMessage);
    }
}

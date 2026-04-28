namespace OnlyRag.Infrastructure.Ingestion;

public sealed class UnavailableOfficeConversionService : IOfficeConversionService
{
    private const string UnavailableMessage = "Installa LibreOffice per Windows oppure configura il path di soffice.exe in Impostazioni.";

    public Task<OfficeConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OfficeConverterAvailability(
            false,
            null,
            "Convertitore Office legacy non disponibile.",
            UnavailableMessage));
    }

    public Task<OfficeConversionResult> ConvertToPdfAsync(
        OfficeConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new OfficeConversionUnavailableException(UnavailableMessage);
    }
}

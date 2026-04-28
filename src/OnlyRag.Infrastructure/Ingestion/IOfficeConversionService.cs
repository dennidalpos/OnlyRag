namespace OnlyRag.Infrastructure.Ingestion;

public interface IOfficeConversionService
{
    Task<OfficeConverterAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<OfficeConversionResult> ConvertToPdfAsync(
        OfficeConversionRequest request,
        CancellationToken cancellationToken = default);
}

using OnlyRag.Core;

namespace OnlyRag.Api;

internal interface IPerformanceSettingsService
{
    Task<PerformanceSettings> GetAsync(CancellationToken cancellationToken = default);

    Task<PerformanceSettings> UpdateAsync(PerformanceSettings settings, CancellationToken cancellationToken = default);
}

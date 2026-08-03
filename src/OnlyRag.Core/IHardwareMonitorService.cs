namespace OnlyRag.Core;

public interface IHardwareMonitorService
{
    HardwareEnergyProfile CurrentProfile { get; }

    Task<HardwareMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    Task SetEnergyProfileAsync(HardwareEnergyProfile profile, CancellationToken cancellationToken = default);

    Task<(int MaxParallelJobs, int DelayMs)> GetThrottledWorkerParametersAsync(
        int configuredMaxParallelJobs,
        CancellationToken cancellationToken = default);
}

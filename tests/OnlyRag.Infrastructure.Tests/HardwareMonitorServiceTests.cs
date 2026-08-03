using OnlyRag.Core;
using OnlyRag.Infrastructure.Hardware;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class HardwareMonitorServiceTests
{
    [Fact]
    public async Task GetMetricsAsync_ReturnsValidMetrics()
    {
        using var service = new HardwareMonitorService();
        HardwareMetrics metrics = await service.GetMetricsAsync();

        Assert.NotNull(metrics);
        Assert.True(metrics.MemoryTotalMB >= 0);
        Assert.True(metrics.MemoryAvailableMB >= 0);
        Assert.True(metrics.CpuUsagePercentage >= 0.0);
        Assert.Equal(HardwareEnergyProfile.Balanced, metrics.ActiveProfile);
    }

    [Fact]
    public async Task SetEnergyProfileAsync_UpdatesActiveProfileAndThrottling()
    {
        using var service = new HardwareMonitorService();
        await service.SetEnergyProfileAsync(HardwareEnergyProfile.Eco);

        HardwareMetrics metrics = await service.GetMetricsAsync();
        Assert.Equal(HardwareEnergyProfile.Eco, metrics.ActiveProfile);

        var (workers, delayMs) = await service.GetThrottledWorkerParametersAsync(4);
        Assert.Equal(1, workers);
        Assert.Equal(500, delayMs);
    }
}

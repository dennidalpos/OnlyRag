namespace OnlyRag.Core;

public enum HardwarePowerSource
{
    ACPower = 0,
    Battery = 1,
    Unknown = 2
}

public enum HardwareLoadLevel
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum HardwareEnergyProfile
{
    Performance = 0,
    Balanced = 1,
    Eco = 2
}

public sealed record HardwareMetrics(
    double CpuUsagePercentage,
    long MemoryAvailableMB,
    long MemoryTotalMB,
    HardwarePowerSource PowerSource,
    int? BatteryPercentage,
    HardwareLoadLevel LoadLevel,
    bool EnergySaverActive,
    HardwareEnergyProfile ActiveProfile,
    int RecommendedMaxWorkers,
    int RecommendedDelayMs,
    DateTimeOffset SampledAt);

public sealed record SetEnergyProfileRequest(HardwareEnergyProfile Profile);

using System.Diagnostics;
using System.Runtime.InteropServices;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Hardware;

public sealed class HardwareMonitorService : IHardwareMonitorService, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HardwareMetrics? _lastMetrics;
    private DateTimeOffset _lastSampleTime = DateTimeOffset.MinValue;
    private readonly TimeSpan _sampleCacheDuration = TimeSpan.FromSeconds(2);

    private long _lastIdleTime;
    private long _lastKernelTime;
    private long _lastUserTime;

    public HardwareEnergyProfile CurrentProfile { get; private set; } = IsTestEnvironment() ? HardwareEnergyProfile.Performance : HardwareEnergyProfile.Balanced;

    private static bool IsTestEnvironment()
    {
        return Environment.GetEnvironmentVariable("ONLYRAG_TEST_ENVIRONMENT") == "true";
    }

    public HardwareMonitorService()
    {
        InitializeCpuSampling();
    }

    public Task SetEnergyProfileAsync(HardwareEnergyProfile profile, CancellationToken cancellationToken = default)
    {
        CurrentProfile = profile;
        _lastMetrics = null; // Invalidate cached metrics so UI updates immediately
        return Task.CompletedTask;
    }

    public async Task<HardwareMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_lastMetrics != null && DateTimeOffset.UtcNow - _lastSampleTime < _sampleCacheDuration)
            {
                return _lastMetrics;
            }

            double cpu = SampleCpuUsage();
            var (memAvailMB, memTotalMB) = GetMemoryInfo();
            var (powerSource, batteryPct, energySaver) = GetPowerStatus();

            HardwareLoadLevel load = HardwareLoadLevel.Normal;
            if (cpu >= 90 || memAvailMB < 512)
            {
                load = HardwareLoadLevel.Critical;
            }
            else if (cpu >= 75 || powerSource == HardwarePowerSource.Battery || energySaver)
            {
                load = HardwareLoadLevel.High;
            }
            else if (cpu <= 25)
            {
                load = HardwareLoadLevel.Low;
            }

            var (recWorkers, recDelay) = CalculateThrottling(4, load, powerSource, energySaver, CurrentProfile);

            _lastMetrics = new HardwareMetrics(
                Math.Round(cpu, 1),
                memAvailMB,
                memTotalMB,
                powerSource,
                batteryPct,
                load,
                energySaver,
                CurrentProfile,
                recWorkers,
                recDelay,
                DateTimeOffset.UtcNow);

            _lastSampleTime = DateTimeOffset.UtcNow;
            return _lastMetrics;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(int MaxParallelJobs, int DelayMs)> GetThrottledWorkerParametersAsync(
        int configuredMaxParallelJobs,
        CancellationToken cancellationToken = default)
    {
        HardwareMetrics metrics = await GetMetricsAsync(cancellationToken);
        return CalculateThrottling(configuredMaxParallelJobs, metrics.LoadLevel, metrics.PowerSource, metrics.EnergySaverActive, CurrentProfile);
    }

    private static (int MaxWorkers, int DelayMs) CalculateThrottling(
        int configured,
        HardwareLoadLevel load,
        HardwarePowerSource powerSource,
        bool energySaver,
        HardwareEnergyProfile profile)
    {
        if (profile == HardwareEnergyProfile.Eco)
        {
            return (1, 500);
        }

        if (profile == HardwareEnergyProfile.Performance)
        {
            int perfWorkers = Math.Max(1, configured);
            return (perfWorkers, 10);
        }

        // Balanced profile
        if (load == HardwareLoadLevel.Critical)
        {
            return (1, 1000);
        }

        if (load == HardwareLoadLevel.High || powerSource == HardwarePowerSource.Battery || energySaver)
        {
            int throttledWorkers = Math.Max(1, configured / 2);
            return (throttledWorkers, 250);
        }

        return (Math.Max(1, configured), 50);
    }

    private void InitializeCpuSampling()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                GetSystemTimes(out long idle, out long kernel, out long user);
                _lastIdleTime = idle;
                _lastKernelTime = kernel;
                _lastUserTime = user;
            }
            catch
            {
                // Fallback
            }
        }
    }

    private double SampleCpuUsage()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
                {
                    long usr = userTime - _lastUserTime;
                    long ker = kernelTime - _lastKernelTime;
                    long idl = idleTime - _lastIdleTime;

                    long sys = usr + ker;

                    _lastIdleTime = idleTime;
                    _lastKernelTime = kernelTime;
                    _lastUserTime = userTime;

                    if (sys > 0)
                    {
                        double cpuPct = ((double)(sys - idl) / sys) * 100.0;
                        return Math.Clamp(cpuPct, 0.0, 100.0);
                    }
                }
            }
            catch
            {
                // Fallback
            }
        }

        return 15.0;
    }

    private static (long AvailableMB, long TotalMB) GetMemoryInfo()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long totalMB = info.TotalAvailableMemoryBytes / (1024 * 1024);
        long sysMemoryMB = Environment.WorkingSet / (1024 * 1024);
        long availableMB = Math.Max(128, totalMB - sysMemoryMB);
        return (availableMB, totalMB);
    }

    private static (HardwarePowerSource PowerSource, int? BatteryPct, bool EnergySaver) GetPowerStatus()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    HardwarePowerSource source = status.ACLineStatus switch
                    {
                        1 => HardwarePowerSource.ACPower,
                        0 => HardwarePowerSource.Battery,
                        _ => HardwarePowerSource.Unknown
                    };

                    int? batteryPct = status.BatteryLifePercent != 255 ? status.BatteryLifePercent : null;
                    bool energySaver = (status.SystemStatusFlag & 1) != 0;
                    return (source, batteryPct, energySaver);
                }
            }
            catch
            {
                // Native call failed
            }
        }

        return (HardwarePowerSource.ACPower, null, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    public void Dispose()
    {
        _lock.Dispose();
    }
}

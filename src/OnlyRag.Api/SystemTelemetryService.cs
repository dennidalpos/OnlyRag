using System.Globalization;
using System.Runtime.InteropServices;
using OnlyRag.Core;

namespace OnlyRag.Api;

public sealed class SystemTelemetryService
{
    private const int CpuSampleDelayMs = 250;
    private readonly ILocalProcessLauncher processLauncher;

    public SystemTelemetryService(ILocalProcessLauncher processLauncher)
    {
        this.processLauncher = processLauncher;
    }

    public async Task<SystemTelemetryResponse> CaptureAsync(CancellationToken cancellationToken = default)
    {
        Task<double?> cpuUsageTask = CaptureCpuUsageAsync(cancellationToken);
        Task<GpuTelemetryResponse?> gpuTask = CaptureGpuTelemetryAsync(cancellationToken);

        return new SystemTelemetryResponse(
            new CpuTelemetryResponse(Environment.ProcessorCount, await cpuUsageTask),
            CaptureMemoryTelemetry(),
            CaptureSystemDiskTelemetry(),
            await gpuTask);
    }

    private static async Task<double?> CaptureCpuUsageAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !TryReadCpuTimes(out CpuTimes start))
        {
            return null;
        }

        await Task.Delay(CpuSampleDelayMs, cancellationToken);
        if (!TryReadCpuTimes(out CpuTimes end))
        {
            return null;
        }

        ulong idle = end.Idle - start.Idle;
        ulong total = end.Total - start.Total;
        if (total == 0 || idle > total)
        {
            return null;
        }

        return Math.Round((1d - (double)idle / total) * 100d, 1);
    }

    private static MemoryTelemetryResponse CaptureMemoryTelemetry()
    {
        if (OperatingSystem.IsWindows() && TryReadMemoryStatus(out MemoryStatusEx memoryStatus))
        {
            return new MemoryTelemetryResponse(
                (long)memoryStatus.TotalPhys,
                (long)memoryStatus.AvailPhys);
        }

        GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();
        long total = Math.Max(0, memoryInfo.TotalAvailableMemoryBytes);
        return new MemoryTelemetryResponse(total, 0);
    }

    private static DiskTelemetryResponse CaptureSystemDiskTelemetry()
    {
        string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            ?? Path.GetPathRoot(Environment.CurrentDirectory)
            ?? "C:\\";
        DriveInfo drive = new(root);
        return new DiskTelemetryResponse(
            drive.Name,
            drive.TotalSize,
            drive.AvailableFreeSpace);
    }

    private async Task<GpuTelemetryResponse?> CaptureGpuTelemetryAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? nvidiaSmiPath = OcrProvisionRuntimeResolver.ResolveExecutable("nvidia-smi");
        if (nvidiaSmiPath is null)
        {
            return null;
        }

        LocalProcessResult result = await processLauncher.RunAsync(
            nvidiaSmiPath,
            [
                "--query-gpu=name,driver_version,utilization.gpu,memory.total,memory.free",
                "--format=csv,noheader,nounits"
            ],
            null,
            cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        string[] fields = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(string.Empty)
            .Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length < 5)
        {
            return null;
        }

        return new GpuTelemetryResponse(
            fields[0],
            fields[1],
            ParseDouble(fields[2]),
            ParseMiB(fields[3]),
            ParseMiB(fields[4]));
    }

    private static double? ParseDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }

    private static long? ParseMiB(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed * 1024L * 1024L
            : null;
    }

    private static bool TryReadCpuTimes(out CpuTimes times)
    {
        times = default;
        if (!GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime))
        {
            return false;
        }

        ulong idle = ToUInt64(idleTime);
        ulong kernel = ToUInt64(kernelTime);
        ulong user = ToUInt64(userTime);
        times = new CpuTimes(idle, kernel + user);
        return true;
    }

    private static bool TryReadMemoryStatus(out MemoryStatusEx memoryStatus)
    {
        memoryStatus = new MemoryStatusEx();
        memoryStatus.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref memoryStatus);
    }

    private static ulong ToUInt64(FileTime value)
    {
        return ((ulong)value.HighDateTime << 32) | value.LowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    private readonly record struct CpuTimes(ulong Idle, ulong Total);
}

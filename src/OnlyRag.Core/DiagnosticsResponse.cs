namespace OnlyRag.Core;

public sealed record DiagnosticsResponse(
    string AppVersion,
    string DatabasePath,
    string LogsDirectory,
    string OllamaStatus,
    bool OllamaIsReachable,
    string OcrStatus,
    bool OcrIsConfigured,
    string OcrEngineName,
    OcrGpuCapabilityResponse OcrGpuCapability,
    SystemTelemetryResponse SystemTelemetry);

public sealed record OcrGpuCapabilityResponse(
    bool IsUsable,
    string Status,
    string? BlockReason,
    string? RuntimeDetail,
    string? EngineVersion,
    string? NvidiaName,
    string? DriverVersion,
    bool? CompiledWithCuda,
    int? CudaDeviceCount,
    string? ActiveDevice,
    IReadOnlyDictionary<string, string> PackageVersions);

public sealed record SystemTelemetryResponse(
    CpuTelemetryResponse Cpu,
    MemoryTelemetryResponse Memory,
    DiskTelemetryResponse SystemDisk,
    GpuTelemetryResponse? Gpu);

public sealed record CpuTelemetryResponse(
    int LogicalProcessorCount,
    double? UsagePercent);

public sealed record MemoryTelemetryResponse(
    long TotalBytes,
    long AvailableBytes);

public sealed record DiskTelemetryResponse(
    string Name,
    long TotalBytes,
    long AvailableBytes);

public sealed record GpuTelemetryResponse(
    string Name,
    string DriverVersion,
    double? UsagePercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes);

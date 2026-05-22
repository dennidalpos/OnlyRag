using System.Text.RegularExpressions;

namespace OnlyRag.Api;

internal sealed class OcrProvisionRuntimeResolver
{
    internal const string AutoTarget = "auto";
    internal const string CpuTarget = "cpu";
    internal const string NvidiaTarget = "nvidia";

    private static readonly Version Cuda129MinimumDriver = new(576, 2);
    private static readonly Version Cuda126MinimumDriver = new(560, 94);
    private static readonly Version Cuda118MinimumDriver = new(520, 6);

    private readonly ILocalProcessLauncher processLauncher;
    private readonly Func<string, string?> resolveExecutable;

    public OcrProvisionRuntimeResolver(ILocalProcessLauncher processLauncher)
        : this(processLauncher, ResolveExecutable)
    {
    }

    internal OcrProvisionRuntimeResolver(
        ILocalProcessLauncher processLauncher,
        Func<string, string?> resolveExecutable)
    {
        this.processLauncher = processLauncher;
        this.resolveExecutable = resolveExecutable;
    }

    public async Task<OcrProvisionRuntime> ResolveAsync(
        string? requestedTarget,
        CancellationToken cancellationToken)
    {
        string target = NormalizeTarget(requestedTarget);
        if (target is CpuTarget)
        {
            return OcrProvisionRuntime.Cpu("CPU richiesta esplicitamente.");
        }

        string? nvidiaSmiPath = resolveExecutable("nvidia-smi");
        if (nvidiaSmiPath is null)
        {
            return target is NvidiaTarget
                ? throw new InvalidOperationException("Runtime NVIDIA richiesto, ma nvidia-smi non e' stato trovato.")
                : OcrProvisionRuntime.Cpu("NVIDIA non rilevata: nvidia-smi non trovato.");
        }

        LocalProcessResult result = await processLauncher.RunAsync(
            nvidiaSmiPath,
            ["--query-gpu=driver_version,name", "--format=csv,noheader"],
            null,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            return target is NvidiaTarget
                ? throw new InvalidOperationException($"Runtime NVIDIA richiesto, ma nvidia-smi non e' utilizzabile: {detail.Trim()}.")
                : OcrProvisionRuntime.Cpu("NVIDIA non utilizzabile: nvidia-smi non ha completato la verifica.");
        }

        NvidiaRuntimeInfo info = ParseNvidiaSmi(result.StandardOutput);
        if (info.DriverVersion is null)
        {
            return target is NvidiaTarget
                ? throw new InvalidOperationException("Runtime NVIDIA richiesto, ma la versione driver non e' stata rilevata.")
                : OcrProvisionRuntime.Cpu("NVIDIA rilevata, ma versione driver non leggibile.");
        }

        if (info.DriverVersion >= Cuda129MinimumDriver)
        {
            return OcrProvisionRuntime.Nvidia(
                "cuda129",
                "requirements-nvidia-cu129.txt",
                $"NVIDIA {info.DisplayName} con driver {info.DriverVersion} compatibile con CUDA 12.9.");
        }

        if (info.DriverVersion >= Cuda126MinimumDriver)
        {
            return OcrProvisionRuntime.Nvidia(
                "cuda126",
                "requirements-nvidia-cu126.txt",
                $"NVIDIA {info.DisplayName} con driver {info.DriverVersion} compatibile con CUDA 12.6.");
        }

        if (info.DriverVersion >= Cuda118MinimumDriver)
        {
            return OcrProvisionRuntime.Nvidia(
                "cuda118",
                "requirements-nvidia-cu118.txt",
                $"NVIDIA {info.DisplayName} con driver {info.DriverVersion} compatibile con CUDA 11.8.");
        }

        string message =
            $"Driver NVIDIA {info.DriverVersion} sotto il minimo supportato per PaddleOCR GPU Windows.";
        return target is NvidiaTarget
            ? throw new InvalidOperationException(message)
            : OcrProvisionRuntime.Cpu(message);
    }

    internal static string NormalizeTarget(string? value)
    {
        string normalized = (value ?? AutoTarget).Trim().ToLowerInvariant();
        return normalized is AutoTarget or CpuTarget or NvidiaTarget
            ? normalized
            : AutoTarget;
    }

    internal static NvidiaRuntimeInfo ParseNvidiaSmi(string output)
    {
        string firstLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        Match versionMatch = Regex.Match(firstLine, @"\d+(?:\.\d+){1,2}");
        Version? driverVersion = versionMatch.Success ? Version.Parse(versionMatch.Value) : null;
        string displayName = firstLine;
        int commaIndex = firstLine.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex >= 0 && commaIndex < firstLine.Length - 1)
        {
            displayName = firstLine[(commaIndex + 1)..].Trim();
        }

        return new NvidiaRuntimeInfo(driverVersion, string.IsNullOrWhiteSpace(displayName) ? "GPU" : displayName);
    }

    internal static string? ResolveExecutable(string executableName)
    {
        string normalizedName = executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName
            : executableName + ".exe";

        IEnumerable<string> candidateDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        candidateDirectories = candidateDirectories.Concat([
            Environment.GetFolderPath(Environment.SpecialFolder.System)
        ]);

        foreach (string directory in candidateDirectories)
        {
            string candidate = Path.Combine(directory, normalizedName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

internal sealed record OcrProvisionRuntime(
    string Target,
    string RequirementsFileName,
    string ResolvedRuntime,
    string Detail)
{
    public bool IsNvidia => Target == OcrProvisionRuntimeResolver.NvidiaTarget;

    public static OcrProvisionRuntime Cpu(string detail)
    {
        return new OcrProvisionRuntime(
            OcrProvisionRuntimeResolver.CpuTarget,
            "requirements-cpu.txt",
            "cpu",
            detail);
    }

    public static OcrProvisionRuntime Nvidia(string runtime, string requirementsFileName, string detail)
    {
        return new OcrProvisionRuntime(
            OcrProvisionRuntimeResolver.NvidiaTarget,
            requirementsFileName,
            runtime,
            detail);
    }
}

internal sealed record NvidiaRuntimeInfo(Version? DriverVersion, string DisplayName);

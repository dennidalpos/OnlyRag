using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OnlyRag.Api;

internal sealed class OcrProvisionRuntimeResolver
{
    internal const string AutoTarget = "auto";
    internal const string CpuTarget = "cpu";
    internal const string NvidiaTarget = "nvidia";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILocalProcessLauncher processLauncher;
    private readonly Func<string, string?> resolveExecutable;
    private readonly string? manifestPath;

    public OcrProvisionRuntimeResolver(ILocalProcessLauncher processLauncher)
        : this(processLauncher, ResolveExecutable, null)
    {
    }

    internal OcrProvisionRuntimeResolver(
        ILocalProcessLauncher processLauncher,
        Func<string, string?> resolveExecutable,
        string? manifestPath = null)
    {
        this.processLauncher = processLauncher;
        this.resolveExecutable = resolveExecutable;
        this.manifestPath = manifestPath;
    }

    public async Task<OcrProvisionRuntime> ResolveAsync(
        string? requestedTarget,
        CancellationToken cancellationToken)
    {
        string target = NormalizeTarget(requestedTarget);
        OcrRuntimeManifest manifest = LoadManifest(manifestPath);
        OcrRuntimeTarget cpuTarget = manifest.ResolveCpuTarget();
        if (target is CpuTarget)
        {
            return OcrProvisionRuntime.Cpu(
                "CPU richiesta esplicitamente.",
                cpuTarget.RequirementsFile,
                cpuTarget.ResolvedRuntime);
        }

        string? nvidiaSmiPath = resolveExecutable("nvidia-smi");
        if (nvidiaSmiPath is null)
        {
            return target is NvidiaTarget
                ? throw new InvalidOperationException("Runtime NVIDIA richiesto, ma nvidia-smi non e' stato trovato.")
                : OcrProvisionRuntime.Cpu(
                    "NVIDIA non rilevata: nvidia-smi non trovato.",
                    cpuTarget.RequirementsFile,
                    cpuTarget.ResolvedRuntime);
        }

        NvidiaRuntimeInfo info;
        try
        {
            info = await QueryNvidiaRuntimeInfoAsync(nvidiaSmiPath, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            if (target is NvidiaTarget)
            {
                throw;
            }

            return OcrProvisionRuntime.Cpu(
                UserFacingErrorText.FromExternalDetail(
                    ex.Message,
                    "NVIDIA rilevata, ma nvidia-smi non ha completato la verifica. OCR CPU selezionato."),
                cpuTarget.RequirementsFile,
                cpuTarget.ResolvedRuntime);
        }
        if (info.DriverVersion is null)
        {
            return target is NvidiaTarget
                ? throw new InvalidOperationException("Runtime NVIDIA richiesto, ma la versione driver non e' stata rilevata.")
                : OcrProvisionRuntime.Cpu(
                    "NVIDIA rilevata, ma versione driver non leggibile.",
                    cpuTarget.RequirementsFile,
                    cpuTarget.ResolvedRuntime);
        }

        if (IsNvidiaSeries50(info.DisplayName))
        {
            string message =
                $"NVIDIA {info.DisplayName} rilevata, ma il supporto PaddlePaddle Windows per RTX serie 50 e' ancora marcato sperimentale/speciale. OnlyRag usera' OCR CPU finche' non c'e' una wheel stabile verificata.";
            return target is NvidiaTarget
                ? throw new InvalidOperationException(message)
                : OcrProvisionRuntime.Cpu(message, cpuTarget.RequirementsFile, cpuTarget.ResolvedRuntime);
        }

        IEnumerable<OcrRuntimeTarget> nvidiaTargets = manifest.RuntimeTargets
            .Where(item => string.Equals(item.Target, NvidiaTarget, StringComparison.OrdinalIgnoreCase))
            .Where(item => !string.IsNullOrWhiteSpace(item.RequirementsFile))
            .OrderByDescending(item => item.MinimumWindowsDriverVersion ?? new Version(0, 0))
            .ThenByDescending(item => item.ResolvedRuntime, StringComparer.OrdinalIgnoreCase);

        foreach (OcrRuntimeTarget runtime in nvidiaTargets)
        {
            if (runtime.MinimumWindowsDriverVersion is Version minimumDriver && info.DriverVersion < minimumDriver)
            {
                continue;
            }

            if (runtime.MinimumComputeCapabilityVersion is Version minimumCapability
                && info.ComputeCapability is Version computeCapability
                && computeCapability < minimumCapability)
            {
                continue;
            }

            string computeDetail = info.ComputeCapability is null
                ? "compute capability non letta da nvidia-smi"
                : $"compute capability {info.ComputeCapability}";
            return OcrProvisionRuntime.Nvidia(
                runtime.ResolvedRuntime,
                runtime.RequirementsFile,
                $"NVIDIA {info.DisplayName} con driver {info.DriverVersion} e {computeDetail} compatibile con {runtime.ResolvedRuntime}.",
                info.DisplayName,
                info.DriverVersion.ToString(),
                info.ComputeCapability?.ToString());
        }

        string detail =
            $"Driver NVIDIA {info.DriverVersion} o compute capability {info.ComputeCapability?.ToString() ?? "non rilevata"} sotto il minimo dei runtime PaddleOCR GPU Windows verificati.";
        return target is NvidiaTarget
            ? throw new InvalidOperationException(detail)
            : OcrProvisionRuntime.Cpu(detail, cpuTarget.RequirementsFile, cpuTarget.ResolvedRuntime);
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
        string[] parts = firstLine.Split(',', StringSplitOptions.TrimEntries);
        Version? driverVersion = ParseVersion(parts.ElementAtOrDefault(0) ?? firstLine);
        string displayName = parts.ElementAtOrDefault(1) ?? firstLine;
        Version? computeCapability = ParseVersion(parts.ElementAtOrDefault(2) ?? string.Empty);

        return new NvidiaRuntimeInfo(
            driverVersion,
            string.IsNullOrWhiteSpace(displayName) ? "GPU" : displayName,
            computeCapability);
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

    internal static OcrRuntimeManifest LoadManifest(string? explicitManifestPath = null)
    {
        string path = explicitManifestPath ?? ResolveDefaultManifestPath();
        if (File.Exists(path))
        {
            OcrRuntimeManifest? manifest = JsonSerializer.Deserialize<OcrRuntimeManifest>(
                File.ReadAllText(path),
                JsonOptions);
            if (manifest?.RuntimeTargets.Count > 0)
            {
                return manifest;
            }
        }

        return OcrRuntimeManifest.Default;
    }

    private async Task<NvidiaRuntimeInfo> QueryNvidiaRuntimeInfoAsync(
        string nvidiaSmiPath,
        CancellationToken cancellationToken)
    {
        LocalProcessResult result = await processLauncher.RunAsync(
            nvidiaSmiPath,
            ["--query-gpu=driver_version,name,compute_cap", "--format=csv,noheader"],
            null,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            result = await processLauncher.RunAsync(
                nvidiaSmiPath,
                ["--query-gpu=driver_version,name", "--format=csv,noheader"],
                null,
                cancellationToken);
        }

        if (result.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException($"Runtime NVIDIA richiesto, ma nvidia-smi non e' utilizzabile: {detail.Trim()}.");
        }

        return ParseNvidiaSmi(result.StandardOutput);
    }

    private static bool IsNvidiaSeries50(string displayName)
    {
        return Regex.IsMatch(displayName, @"\bRTX\s+50\d{2}\b|\bRTX\s+5\d{3}\b", RegexOptions.IgnoreCase);
    }

    private static Version? ParseVersion(string text)
    {
        Match match = Regex.Match(text, @"\d+(?:\.\d+){0,2}");
        if (!match.Success)
        {
            return null;
        }

        string[] parts = match.Value.Split('.');
        return new Version(
            int.Parse(parts[0]),
            parts.Length > 1 ? int.Parse(parts[1]) : 0,
            parts.Length > 2 ? int.Parse(parts[2]) : 0);
    }

    private static string ResolveDefaultManifestPath()
    {
        string outputManifest = Path.Combine(AppContext.BaseDirectory, "scripts", "ocr", "runtime-manifest.json");
        if (File.Exists(outputManifest))
        {
            return outputManifest;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "scripts",
            "ocr",
            "runtime-manifest.json"));
    }
}

internal sealed record OcrProvisionRuntime(
    string Target,
    string RequirementsFileName,
    string ResolvedRuntime,
    string Detail,
    string? NvidiaName = null,
    string? DriverVersion = null,
    string? ComputeCapability = null)
{
    public bool IsNvidia => Target == OcrProvisionRuntimeResolver.NvidiaTarget;

    public static OcrProvisionRuntime Cpu(
        string detail,
        string requirementsFileName = "requirements-cpu.txt",
        string resolvedRuntime = "cpu")
    {
        return new OcrProvisionRuntime(
            OcrProvisionRuntimeResolver.CpuTarget,
            requirementsFileName,
            resolvedRuntime,
            detail);
    }

    public static OcrProvisionRuntime Nvidia(
        string runtime,
        string requirementsFileName,
        string detail,
        string? nvidiaName = null,
        string? driverVersion = null,
        string? computeCapability = null)
    {
        return new OcrProvisionRuntime(
            OcrProvisionRuntimeResolver.NvidiaTarget,
            requirementsFileName,
            runtime,
            detail,
            nvidiaName,
            driverVersion,
            computeCapability);
    }
}

internal sealed record NvidiaRuntimeInfo(Version? DriverVersion, string DisplayName, Version? ComputeCapability);

internal sealed record OcrRuntimeManifest(
    [property: JsonPropertyName("runtimeTargets")] IReadOnlyList<OcrRuntimeTarget> RuntimeTargets)
{
    public static OcrRuntimeManifest Default { get; } = new([
        new OcrRuntimeTarget("cpu", "requirements-cpu.txt", "cpu", null, null),
        new OcrRuntimeTarget("nvidia", "requirements-nvidia-cu129.txt", "cuda129", "576.02", "7.5"),
        new OcrRuntimeTarget("nvidia", "requirements-nvidia-cu126.txt", "cuda126", "560.94", "7.5"),
        new OcrRuntimeTarget("nvidia", "requirements-nvidia-cu118.txt", "cuda118", "520.06", "7.5")
    ]);

    public OcrRuntimeTarget ResolveCpuTarget()
    {
        return RuntimeTargets.FirstOrDefault(item => string.Equals(item.Target, "cpu", StringComparison.OrdinalIgnoreCase))
            ?? new OcrRuntimeTarget("cpu", "requirements-cpu.txt", "cpu", null, null);
    }
}

internal sealed record OcrRuntimeTarget(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("requirementsFile")] string RequirementsFile,
    [property: JsonPropertyName("resolvedRuntime")] string ResolvedRuntime,
    [property: JsonPropertyName("minimumWindowsDriver")] string? MinimumWindowsDriver,
    [property: JsonPropertyName("minimumComputeCapability")] string? MinimumComputeCapability)
{
    public Version? MinimumWindowsDriverVersion => ParseManifestVersion(MinimumWindowsDriver);

    public Version? MinimumComputeCapabilityVersion => ParseManifestVersion(MinimumComputeCapability);

    private static Version? ParseManifestVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts.ElementAtOrDefault(0), out int major))
        {
            return null;
        }

        _ = int.TryParse(parts.ElementAtOrDefault(1), out int minor);
        _ = int.TryParse(parts.ElementAtOrDefault(2), out int build);
        return new Version(major, minor, build);
    }
}

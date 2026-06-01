using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Api;

public sealed partial class DependencyProvisioningService
{
    private async Task<OcrPythonCommand> ResolveOcrPythonCommandAsync(CancellationToken cancellationToken)
    {
        List<string> unsupported = [];
        foreach (OcrPythonCommand candidate in OcrPythonRuntime.ResolveCandidates(executableResolver))
        {
            LocalProcessResult result = await processLauncher.RunAsync(
                candidate.FileName,
                candidate.WithArguments(["--version"]),
                null,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                continue;
            }

            string versionText = OcrPythonRuntime.GetVersionText(result);
            Version? version = ParsePythonVersion(versionText);
            if (version is null)
            {
                continue;
            }

            if (IsSupportedOcrPythonVersion(version))
            {
                return candidate;
            }

            unsupported.Add(versionText);
        }

        string detail = unsupported.Count == 0
            ? "Python compatibile non trovato."
            : $"Interpreti non compatibili trovati: {string.Join(", ", unsupported)}.";
        throw new InvalidOperationException(
            "OCR richiede Python 3.10, 3.11, 3.12 o 3.13. " +
            "PaddlePaddle 3.3.1 non pubblica wheel Windows per Python 3.14. " +
            $"{detail} Installa una versione compatibile di Python per Windows, poi ripeti Installa OCR.");
    }

    private string ResolveOcrScriptsRoot()
    {
        if (ocrScriptsRootOverride is not null)
        {
            return ocrScriptsRootOverride;
        }

        string outputRoot = Path.Combine(AppContext.BaseDirectory, "scripts", "ocr");
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "ocr"));
    }

    private string ResolveOcrInstallRoot()
    {
        if (ocrInstallRootOverride is not null)
        {
            return ocrInstallRootOverride;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "OnlyRag", "ocr-python");
    }

    private async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        LocalProcessResult result = await processLauncher.RunAsync(fileName, arguments, workingDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"{fileName} terminato con exit code {result.ExitCode}."
                : detail.Trim());
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
    }

    private async Task<string> RunPaddlePackageCleanupAsync(
        string venvPython,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        string[] arguments = ["-m", "pip", "uninstall", "-y", "paddlepaddle", "paddlepaddle-gpu"];
        LocalProcessResult result = await processLauncher.RunAsync(venvPython, arguments, workingDirectory, cancellationToken);
        string detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        if (result.ExitCode == 0 || IsBenignPaddlePackageCleanupOutput(detail))
        {
            return string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardError : result.StandardOutput;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"{venvPython} terminato con exit code {result.ExitCode}."
            : detail.Trim());
    }

    private static bool IsBenignPaddlePackageCleanupOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        return output.Contains("Skipping paddlepaddle", StringComparison.OrdinalIgnoreCase)
            && output.Contains("not installed", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        return timeout.TotalMinutes >= 1
            ? $"{(int)Math.Ceiling(timeout.TotalMinutes)} minuti"
            : $"{(int)Math.Ceiling(timeout.TotalSeconds)} secondi";
    }

    private static int CalculateProgressPercent(int stepIndex)
    {
        return Math.Clamp((int)Math.Round(stepIndex * 100d / OcrProvisionStepCount), 0, 99);
    }

    internal static Version? ParsePythonVersion(string text)
    {
        return OcrPythonRuntime.ParseVersion(text);
    }

    internal static bool IsSupportedOcrPythonVersion(Version version)
    {
        return OcrPythonRuntime.IsSupportedVersion(version);
    }
}

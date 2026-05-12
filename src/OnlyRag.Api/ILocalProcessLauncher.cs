using System.Diagnostics;

namespace OnlyRag.Api;

public sealed record LocalProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface ILocalProcessLauncher
{
    bool TryStart(ProcessStartInfo startInfo, out string? errorMessage);

    Task<LocalProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken);
}

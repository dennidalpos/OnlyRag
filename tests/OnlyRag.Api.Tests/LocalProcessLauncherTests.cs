using System.Text;
using OnlyRag.Api;

namespace OnlyRag.Api.Tests;

public sealed class LocalProcessLauncherTests
{
    [Fact]
    public async Task RunAsync_KillsProcessTreeWhenCancelled()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.ProcessLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string markerPath = Path.Combine(root, "cancel-marker.txt");

        try
        {
            LocalProcessLauncher launcher = new();
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(200));
            string command = $"Start-Sleep -Seconds 10; Set-Content -LiteralPath '{EscapePowerShellSingleQuotedString(markerPath)}' -Value done";
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                launcher.RunAsync(
                    ResolvePowerShellExecutable(),
                    ["-NoProfile", "-NonInteractive", "-EncodedCommand", encodedCommand],
                    workingDirectory: null,
                    cancellation.Token));

            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.False(File.Exists(markerPath), "Cancelled child process should not continue after RunAsync returns.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_CapsCapturedOutput()
    {
        LocalProcessLauncher launcher = new();
        string command = "$text = 'x' * 70000; [Console]::Out.Write($text); [Console]::Error.Write($text)";
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        LocalProcessResult result = await launcher.RunAsync(
            ResolvePowerShellExecutable(),
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", encodedCommand],
            workingDirectory: null,
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StandardOutput.Length <= LocalProcessLauncher.MaxCapturedOutputCharacters + 32);
        Assert.True(result.StandardError.Length <= LocalProcessLauncher.MaxCapturedOutputCharacters + 32);
        Assert.Contains("[output truncated]", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("[output truncated]", result.StandardError, StringComparison.Ordinal);
    }

    private static string ResolvePowerShellExecutable()
    {
        return ResolveExecutable("pwsh.exe") ?? ResolveExecutable("powershell.exe") ?? "pwsh";
    }

    private static string? ResolveExecutable(string executableName)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}

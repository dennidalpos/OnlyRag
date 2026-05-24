using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OnlyRag.Api;

public sealed class LocalProcessLauncher : ILocalProcessLauncher
{
    internal const int MaxCapturedOutputCharacters = 64 * 1024;
    private const string OutputTruncatedMarker = "\n[output truncated]";

    public bool TryStart(ProcessStartInfo startInfo, out string? errorMessage)
    {
        using Process process = new() { StartInfo = startInfo };
        try
        {
            bool started = process.Start();
            errorMessage = started ? null : "Il processo non ha accettato la richiesta di avvio.";
            return started;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public async Task<LocalProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Impossibile avviare {fileName}.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"Impossibile avviare {fileName}: {ex.Message}", ex);
        }

        Task<string> stdoutTask = ReadToEndBoundedAsync(process.StandardOutput, cancellationToken);
        Task<string> stderrTask = ReadToEndBoundedAsync(process.StandardError, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            string[] output = await Task.WhenAll(stdoutTask, stderrTask);
            return new LocalProcessResult(process.ExitCode, output[0], output[1]);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }
    }

    private static async Task<string> ReadToEndBoundedAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        StringBuilder builder = new(capacity: Math.Min(MaxCapturedOutputCharacters, buffer.Length));
        bool truncated = false;

        while (true)
        {
            int read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            int remaining = MaxCapturedOutputCharacters - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }

            if (read > remaining)
            {
                truncated = true;
            }
        }

        if (truncated)
        {
            builder.Append(OutputTruncatedMarker);
        }

        return builder.ToString();
    }

    private static void KillProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
        }
        catch
        {
        }
    }
}

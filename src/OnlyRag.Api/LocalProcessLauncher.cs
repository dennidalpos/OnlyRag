using System.ComponentModel;
using System.Diagnostics;

namespace OnlyRag.Api;

public sealed class LocalProcessLauncher : ILocalProcessLauncher
{
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

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new LocalProcessResult(process.ExitCode, stdout, stderr);
    }
}

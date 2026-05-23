using System.Windows;
using System.Diagnostics;
using System.IO;
using OnlyRag.Api;
using OnlyRag.Core;

namespace OnlyRag.App;

public partial class App : Application
{
    private InProcessBackendHandle? backend;
    private bool terminatePeerProcessesOnExit;
    private bool isDisposingBackend;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? resetError = TryApplyPendingDataReset();
        BackendWebSettings backendSettings = resetError is null
            ? await StartBackendAsync()
            : BackendWebSettings.Offline(resetError);
        MainWindow mainWindow = new(backendSettings);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static string? TryApplyPendingDataReset()
    {
        try
        {
            AppDataReset.ApplyPendingReset(AppStoragePaths.FromLocalAppData());
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string detail = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Dettagli tecnici disponibili nei log locali.");
            return "Reset dati locali non completato. Chiudi eventuali istanze di OnlyRag e riprova. Dettaglio: " + detail;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (backend is not null)
        {
            isDisposingBackend = true;
            try
            {
                backend.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        if (terminatePeerProcessesOnExit)
        {
            TerminatePeerProcesses();
        }

        base.OnExit(e);
    }

    private async Task<BackendWebSettings> StartBackendAsync()
    {
        try
        {
            backend = await InProcessBackend.StartAsync();
            backend.StoppedToken.Register(OnBackendStopped);
            return new BackendWebSettings(
                true,
                backend.BaseUri.ToString(),
                backend.SessionToken,
                OnlyRag.Core.OnlyRagApiHeaders.SessionTokenHeaderName,
                null);
        }
        catch (Exception ex)
        {
            return BackendWebSettings.Offline(UserFacingErrorText.StartupFailure(ex));
        }
    }

    private void OnBackendStopped()
    {
        if (isDisposingBackend)
        {
            return;
        }

        Dispatcher.InvokeAsync(async () =>
        {
            if (MainWindow is MainWindow mainWindow)
            {
                await mainWindow.NotifyBackendOfflineAsync();
            }
        });
    }

    public void EnablePeerProcessTerminationOnExit()
    {
        terminatePeerProcessesOnExit = true;
    }

    private static void TerminatePeerProcesses()
    {
        using Process current = Process.GetCurrentProcess();
        string? currentExecutablePath = TryGetExecutablePath(current);
        if (string.IsNullOrWhiteSpace(currentExecutablePath))
        {
            return;
        }

        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id == current.Id)
            {
                continue;
            }

            try
            {
                string? peerExecutablePath = TryGetExecutablePath(process);
                if (!string.Equals(currentExecutablePath, peerExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool closeRequested = process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow();
                if (!closeRequested || !process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: false);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            string? fileName = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFullPath(fileName);
        }
        catch
        {
            return null;
        }
    }
}

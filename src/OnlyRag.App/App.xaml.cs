using System.Windows;
using System.Diagnostics;
using OnlyRag.Api;

namespace OnlyRag.App;

public partial class App : Application
{
    private InProcessBackendHandle? backend;
    private bool terminatePeerProcessesOnExit;
    private bool isDisposingBackend;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        BackendWebSettings backendSettings = await StartBackendAsync();
        MainWindow mainWindow = new(backendSettings);
        MainWindow = mainWindow;
        mainWindow.Show();
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
            return BackendWebSettings.Offline(ex.Message);
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
        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id == current.Id)
            {
                continue;
            }

            try
            {
                bool closeRequested = process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow();
                if (!closeRequested || !process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
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
}

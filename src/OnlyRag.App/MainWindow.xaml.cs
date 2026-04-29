using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using OnlyRag.Core;

namespace OnlyRag.App;

public partial class MainWindow : Window
{
    private const string DefaultViteDevServerUrl = "http://127.0.0.1:5173/";
    private const string DevServerEnvironmentVariable = "ONLYRAG_WEB_DEV_SERVER";
    private readonly BackendWebSettings backendSettings;
    private bool isExitConfirmed;
    private bool isExitFlowInProgress;

    public MainWindow(BackendWebSettings backendSettings)
    {
        this.backendSettings = backendSettings;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosingAsync;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            await MainWebView.EnsureCoreWebView2Async();
            await MainWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CreateBackendBridgeScript(backendSettings));
            MainWebView.Source = await ResolveStartupUriAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError(
                "WebView2 non e installato",
                "OnlyRag richiede Microsoft Edge WebView2 Runtime per mostrare l'interfaccia. Installa il runtime WebView2 e riavvia l'applicazione.");
        }
        catch (FileNotFoundException ex)
        {
            ShowStartupError(
                "Interfaccia non trovata",
                $"La build statica della UI non e disponibile. Esegui lo script scripts\\Build-Web.ps1 prima di compilare o distribuire l'app.\n\nPercorso atteso: {ex.FileName}");
        }
        catch (Exception ex)
        {
            ShowStartupError(
                "OnlyRag non puo avviare la UI",
                $"Si e verificato un errore durante l'inizializzazione della finestra principale.\n\nDettaglio: {ex.Message}");
        }
    }

    private async Task<Uri> ResolveStartupUriAsync()
    {
#if DEBUG
        Uri devServerUri = GetDevServerUri();
        if (await IsDevServerAvailableAsync(devServerUri))
        {
            return devServerUri;
        }
#endif

        return MapStaticWebRoot();
    }

    private static Uri GetDevServerUri()
    {
        string? configuredUrl = Environment.GetEnvironmentVariable(DevServerEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredUrl)
            && Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri? configuredUri)
            && (configuredUri.Scheme == Uri.UriSchemeHttp || configuredUri.Scheme == Uri.UriSchemeHttps))
        {
            return configuredUri;
        }

        return new Uri(DefaultViteDevServerUrl);
    }

    private static async Task<bool> IsDevServerAvailableAsync(Uri devServerUri)
    {
        using HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromMilliseconds(800)
        };

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(devServerUri, HttpCompletionOption.ResponseHeadersRead);
            return response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.InternalServerError;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private Uri MapStaticWebRoot()
    {
        string webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        string indexPath = Path.Combine(webRoot, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Static web UI build was not found.", indexPath);
        }

        MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            OnlyRagWebOrigins.StaticWebViewHostName,
            webRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);

        return new Uri($"{OnlyRagWebOrigins.StaticWebViewOrigin}/index.html");
    }

    private void ShowStartupError(string title, string message)
    {
        MainWebView.Visibility = Visibility.Collapsed;
        StartupErrorTitle.Text = title;
        StartupErrorMessage.Text = message;
        StartupErrorPanel.Visibility = Visibility.Visible;
    }

    private void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (isExitConfirmed)
        {
            return;
        }

        if (isExitFlowInProgress)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _ = ConfirmAndCloseAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        MainWebView.Dispose();
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (isExitFlowInProgress)
        {
            return;
        }

        isExitFlowInProgress = true;
        string originalTitle = Title;
        Title = originalTitle + " — Chiusura in corso…";

        try
        {
            AppExitState? exitState = await TryGetExitStateAsync();
            int backendActiveJobCount = await TryGetBackendActiveJobCountAsync();
            AppExitState combinedExitState = CombineExitState(exitState, backendActiveJobCount);
            if (!combinedExitState.HasPendingChanges && !combinedExitState.HasActiveWork && combinedExitState.ActiveJobCount == 0)
            {
                ((App)Application.Current).EnablePeerProcessTerminationOnExit();
                ConfirmClose();
                return;
            }

            string message = BuildExitConfirmationMessage(combinedExitState);
            MessageBoxResult decision = MessageBox.Show(
                this,
                message,
                "Conferma uscita",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (decision != MessageBoxResult.Yes)
            {
                return;
            }

            AppExitState? postSaveState = await TryPrepareForExitAsync();
            int backendActiveJobCountAfterPrepare = await TryGetBackendActiveJobCountAsync();
            AppExitState combinedPostSaveState = CombineExitState(postSaveState, backendActiveJobCountAfterPrepare);
            if (combinedPostSaveState.HasPendingChanges)
            {
                MessageBox.Show(
                    this,
                    "OnlyRag non e riuscito a salvare tutte le modifiche prima dell'uscita. Completa o salva il lavoro manualmente e riprova.",
                    "Uscita annullata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            ((App)Application.Current).EnablePeerProcessTerminationOnExit();
            ConfirmClose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"OnlyRag non e riuscito a preparare l'uscita.\n\nDettaglio: {ex.Message}",
                "Uscita annullata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            isExitFlowInProgress = false;
            Title = originalTitle;
        }
    }

    private void ConfirmClose()
    {
        isExitConfirmed = true;
        Dispatcher.InvokeAsync(Close);
    }

    private async Task<AppExitState?> TryGetExitStateAsync()
    {
        if (StartupErrorPanel.Visibility == Visibility.Visible || MainWebView.CoreWebView2 is null)
        {
            return null;
        }

        return await ExecuteExitScriptAsync("getExitState");
    }

    private async Task<AppExitState?> TryPrepareForExitAsync()
    {
        if (StartupErrorPanel.Visibility == Visibility.Visible || MainWebView.CoreWebView2 is null)
        {
            return null;
        }

        await ExecuteExitScriptAsync("prepareForExit");

        AppShutdownPreparationResponse? shutdown = await TryPrepareBackendShutdownAsync();
        if (shutdown is not null && !shutdown.IsComplete)
        {
            throw new InvalidOperationException(
                $"Non e stato possibile fermare tutti i job locali entro il timeout. Job ancora attivi: {string.Join(", ", shutdown.UnstoppedJobIds)}.");
        }

        return await TryGetExitStateAsync();
    }

    private async Task<AppExitState?> ExecuteExitScriptAsync(string methodName)
    {
        string script =
            $$"""
            (async () => {
              if (!globalThis.__ONLYRAG_APP__ || typeof globalThis.__ONLYRAG_APP__.{{methodName}} !== "function") {
                return null;
              }

              return await globalThis.__ONLYRAG_APP__.{{methodName}}();
            })();
            """;

        string raw = await MainWebView.ExecuteScriptAsync(script);
        return string.IsNullOrWhiteSpace(raw) || raw == "null"
            ? null
            : JsonSerializer.Deserialize<AppExitState>(raw, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }

    private async Task<int> TryGetBackendActiveJobCountAsync()
    {
        if (!backendSettings.IsRunning || string.IsNullOrWhiteSpace(backendSettings.BaseUrl))
        {
            return 0;
        }

        try
        {
            using HttpClient httpClient = new()
            {
                BaseAddress = new Uri(backendSettings.BaseUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };

            List<BackendJob>? jobs = await httpClient.GetFromJsonAsync<List<BackendJob>>(
                "/api/jobs?limit=500",
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return jobs?.Count(job => job.Status is "Pending" or "Running" or "Paused") ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static AppExitState CombineExitState(AppExitState? exitState, int backendActiveJobCount)
    {
        AppExitState combined = exitState ?? new AppExitState();
        if (backendActiveJobCount <= combined.ActiveJobCount)
        {
            return combined;
        }

        combined.ActiveJobCount = backendActiveJobCount;
        if (!combined.Reasons.Any(reason => reason.StartsWith("Job locali attivi:", StringComparison.OrdinalIgnoreCase)))
        {
            combined.Reasons.Add($"Job locali attivi: {backendActiveJobCount}.");
        }

        return combined;
    }

    private static string BuildExitConfirmationMessage(AppExitState? exitState)
    {
        if (exitState is null)
        {
            return "OnlyRag chiudera l'applicazione e terminera le altre istanze aperte.\n\nUscire?";
        }

        List<string> lines = [];
        if (exitState.Reasons.Count > 0)
        {
            lines.Add("Sono presenti modifiche o attivita in corso:");
            lines.AddRange(exitState.Reasons.Select(reason => $"- {reason}"));
            lines.Add(string.Empty);
        }

        lines.Add("OnlyRag salvera il lavoro disponibile, cancellera i job locali attivi e terminera le altre istanze aperte dell'app.");
        lines.Add(string.Empty);
        lines.Add("Uscire?");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<AppShutdownPreparationResponse?> TryPrepareBackendShutdownAsync()
    {
        if (!backendSettings.IsRunning || string.IsNullOrWhiteSpace(backendSettings.BaseUrl))
        {
            return null;
        }

        using HttpClient httpClient = new()
        {
            BaseAddress = new Uri(backendSettings.BaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };

        using HttpResponseMessage response = await httpClient.PostAsync("/api/app/prepare-shutdown", content: null);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Preparazione chiusura fallita con stato {(int)response.StatusCode}."
                    : detail);
        }

        return await response.Content.ReadFromJsonAsync<AppShutdownPreparationResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    internal async Task NotifyBackendOfflineAsync()
    {
        if (MainWebView.CoreWebView2 is null)
        {
            return;
        }

        await MainWebView.ExecuteScriptAsync(
            "if (window.__ONLYRAG_BACKEND__) { window.__ONLYRAG_BACKEND__.isRunning = false; }");
    }

    private static string CreateBackendBridgeScript(BackendWebSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return $"window.__ONLYRAG_BACKEND__ = {json};";
    }

    private sealed class AppExitState
    {
        public bool HasPendingChanges { get; set; }

        public bool HasActiveWork { get; set; }

        public int ActiveJobCount { get; set; }

        public List<string> Reasons { get; set; } = [];
    }

    private sealed class BackendJob
    {
        public string Status { get; init; } = string.Empty;
    }

    private sealed class AppShutdownPreparationResponse
    {
        public bool IsComplete { get; init; }

        public List<string> UnstoppedJobIds { get; init; } = [];
    }
}

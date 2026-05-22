using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using OnlyRag.Core;

namespace OnlyRag.App;

public partial class MainWindow : Window
{
    private const string DefaultViteDevServerUrl = "http://127.0.0.1:5173/";
    private const string DevServerEnvironmentVariable = "ONLYRAG_WEB_DEV_SERVER";
    private const int MinimumWindowsBuild = 17763;
    private static readonly JsonSerializerOptions ExitStateJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions BackendBridgeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
            StartupPrerequisiteStatus prerequisites = CheckStartupPrerequisites();
            if (!prerequisites.IsSatisfied)
            {
                ShowStartupError(prerequisites.Title, prerequisites.Message);
                return;
            }

            CoreWebView2Environment webViewEnvironment = await CreateWebViewEnvironmentAsync();
            await MainWebView.EnsureCoreWebView2Async(webViewEnvironment);
            await MainWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CreateBackendBridgeScript(backendSettings));
            MainWebView.Source = await ResolveStartupUriAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError(
                "WebView2 non e installato",
                BuildWebView2MissingMessage());
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

    private static async Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync()
    {
        string userDataFolder = AppStoragePaths.FromLocalAppData().WebView2UserDataDirectory;
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: userDataFolder);
    }

    private static StartupPrerequisiteStatus CheckStartupPrerequisites()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild))
        {
            return StartupPrerequisiteStatus.Blocked(
                "Windows non supportato",
                "OnlyRag non puo avviarsi perche questa versione di Windows non e supportata.\n\n" +
                "- Software: Microsoft Windows\n" +
                "- Versione minima supportata: Windows 10 versione 1809, build 17763, oppure Windows 11\n" +
                "- Perche serve: OnlyRag usa WPF, WebView2 e componenti .NET Windows validati per Windows 10 1809 o versioni successive.\n" +
                "- Istruzione: aggiorna Windows da Impostazioni > Windows Update oppure usa un client Windows 10/11 supportato.\n" +
                "- Verifica: premi Win+R, esegui winver e controlla versione/build.");
        }

        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return StartupPrerequisiteStatus.Blocked(
                "WebView2 non e installato",
                BuildWebView2MissingMessage());
        }

        return StartupPrerequisiteStatus.Satisfied();
    }

    private static string BuildWebView2MissingMessage()
    {
        return
            "OnlyRag non puo avviarsi perche un runtime Windows richiesto non e installato.\n\n" +
            "- Software: Microsoft Edge WebView2 Runtime\n" +
            "- Versione minima supportata: Evergreen Runtime corrente per Windows 10 1809 o versioni successive / Windows 11\n" +
            "- Perche serve: OnlyRag e una app desktop WPF che mostra la UI React inclusa tramite Microsoft WebView2.\n" +
            "- Istruzione: installa il Microsoft Edge WebView2 Evergreen Runtime dal sito ufficiale Microsoft e riavvia OnlyRag.\n" +
            "- Verifica: apri Impostazioni > App e controlla che Microsoft Edge WebView2 Runtime sia presente, oppure verifica msedgewebview2.exe sotto Program Files\\Microsoft\\EdgeWebView\\Application.";
    }

    private static Uri GetDevServerUri()
    {
        string? configuredUrl = Environment.GetEnvironmentVariable(DevServerEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredUrl)
            && Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri? configuredUri)
            && IsAllowedDevServerUri(configuredUri))
        {
            return configuredUri;
        }

        return new Uri(DefaultViteDevServerUrl);
    }

    private static bool IsAllowedDevServerUri(Uri uri)
    {
        return uri.IsLoopback
            && string.IsNullOrWhiteSpace(uri.UserInfo)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
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
            BackendActiveJobSnapshot backendActiveJobs = await TryGetBackendActiveJobSnapshotAsync();
            AppExitState combinedExitState = CombineExitState(exitState, backendActiveJobs);
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
            BackendActiveJobSnapshot backendActiveJobsAfterPrepare = await TryGetBackendActiveJobSnapshotAsync();
            AppExitState combinedPostSaveState = CombineExitState(postSaveState, backendActiveJobsAfterPrepare);
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
            : JsonSerializer.Deserialize<AppExitState>(raw, ExitStateJsonOptions);
    }

    private async Task<BackendActiveJobSnapshot> TryGetBackendActiveJobSnapshotAsync()
    {
        if (!backendSettings.IsRunning || string.IsNullOrWhiteSpace(backendSettings.BaseUrl))
        {
            return BackendActiveJobSnapshot.Known(0);
        }

        try
        {
            using HttpClient httpClient = new()
            {
                BaseAddress = new Uri(backendSettings.BaseUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };
            AddBackendSessionToken(httpClient);

            List<BackendJob>? jobs = await httpClient.GetFromJsonAsync<List<BackendJob>>(
                "/api/jobs?limit=500",
                ExitStateJsonOptions);

            int count = jobs?.Count(job => job.Status is "Pending" or "Running" or "Pausing" or "Paused") ?? 0;
            return BackendActiveJobSnapshot.Known(count);
        }
        catch
        {
            return BackendActiveJobSnapshot.Unknown();
        }
    }

    private static AppExitState CombineExitState(AppExitState? exitState, BackendActiveJobSnapshot backendActiveJobs)
    {
        AppExitState combined = exitState ?? new AppExitState();
        if (!backendActiveJobs.IsKnown)
        {
            combined.IsActiveJobStateUnknown = true;
            combined.HasActiveWork = true;
            if (!combined.Reasons.Any(reason => reason.StartsWith("Stato dei job locali", StringComparison.OrdinalIgnoreCase)))
            {
                combined.Reasons.Add("Stato dei job locali non verificabile.");
            }

            return combined;
        }

        combined.IsActiveJobStateUnknown = false;
        combined.Reasons.RemoveAll(reason => reason.StartsWith("Stato dei job locali", StringComparison.OrdinalIgnoreCase));

        if (backendActiveJobs.Count <= combined.ActiveJobCount)
        {
            if (combined.ActiveJobCount == 0 && !combined.Reasons.Any(reason => reason.Contains("operazione in corso", StringComparison.OrdinalIgnoreCase)))
            {
                combined.HasActiveWork = false;
            }

            return combined;
        }

        combined.ActiveJobCount = backendActiveJobs.Count;
        combined.HasActiveWork = true;
        if (!combined.Reasons.Any(reason => reason.StartsWith("Job locali attivi:", StringComparison.OrdinalIgnoreCase)))
        {
            combined.Reasons.Add($"Job locali attivi: {backendActiveJobs.Count}.");
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
        AddBackendSessionToken(httpClient);

        using HttpResponseMessage response = await httpClient.PostAsync("/api/app/prepare-shutdown", content: null);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Preparazione chiusura fallita con stato {(int)response.StatusCode}."
                    : detail);
        }

        return await response.Content.ReadFromJsonAsync<AppShutdownPreparationResponse>(ExitStateJsonOptions);
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
        string json = JsonSerializer.Serialize(settings, BackendBridgeJsonOptions);

        return $"window.__ONLYRAG_BACKEND__ = {json};";
    }

    private void AddBackendSessionToken(HttpClient httpClient)
    {
        if (!string.IsNullOrWhiteSpace(backendSettings.ApiToken))
        {
            httpClient.DefaultRequestHeaders.Add(backendSettings.ApiTokenHeaderName, backendSettings.ApiToken);
        }
    }

    private sealed class AppExitState
    {
        public bool HasPendingChanges { get; set; }

        public bool HasActiveWork { get; set; }

        public int ActiveJobCount { get; set; }

        public bool IsActiveJobStateUnknown { get; set; }

        public List<string> Reasons { get; set; } = [];
    }

    private sealed class BackendJob
    {
        public string Status { get; init; } = string.Empty;
    }

    private sealed record BackendActiveJobSnapshot(bool IsKnown, int Count)
    {
        public static BackendActiveJobSnapshot Known(int count)
        {
            return new BackendActiveJobSnapshot(true, count);
        }

        public static BackendActiveJobSnapshot Unknown()
        {
            return new BackendActiveJobSnapshot(false, 0);
        }
    }

    private sealed record StartupPrerequisiteStatus(
        bool IsSatisfied,
        string Title,
        string Message)
    {
        public static StartupPrerequisiteStatus Satisfied()
        {
            return new StartupPrerequisiteStatus(true, string.Empty, string.Empty);
        }

        public static StartupPrerequisiteStatus Blocked(string title, string message)
        {
            return new StartupPrerequisiteStatus(false, title, message);
        }
    }

    private sealed class AppShutdownPreparationResponse
    {
        public bool IsComplete { get; init; }

        public List<string> UnstoppedJobIds { get; init; } = [];
    }
}

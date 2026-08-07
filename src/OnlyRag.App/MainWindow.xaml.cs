using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using OnlyRag.Api;
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

    private BackendWebSettings? backendSettings;
    private bool isExitConfirmed;
    private bool isExitFlowInProgress;
    private bool isWebViewInitialized;

    public MainWindow(BackendWebSettings? backendSettings = null)
    {
        this.backendSettings = backendSettings;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosingAsync;
        Closed += OnClosed;
    }

    public async Task InitializeBackendSettingsAsync(BackendWebSettings settings)
    {
        this.backendSettings = settings;
        if (IsLoaded)
        {
            await BindBackendAndShowWebViewAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (backendSettings is not null)
        {
            await BindBackendAndShowWebViewAsync();
        }
    }

    private async Task BindBackendAndShowWebViewAsync()
    {
        if (backendSettings is null || isWebViewInitialized)
        {
            return;
        }

        try
        {
            StartupPrerequisiteStatus prerequisites = CheckStartupPrerequisites();
            if (!prerequisites.IsSatisfied)
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                ShowStartupError(prerequisites.Title, prerequisites.Message);
                return;
            }

            CoreWebView2Environment webViewEnvironment = await CreateWebViewEnvironmentAsync();
            await MainWebView.EnsureCoreWebView2Async(webViewEnvironment);
            await MainWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CreateBackendBridgeScript(backendSettings));
            MainWebView.Source = await ResolveStartupUriAsync();
            isWebViewInitialized = true;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ShowStartupError(
                "WebView2 non e installato",
                BuildWebView2MissingMessage());
        }
        catch (FileNotFoundException ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            string detail = UserFacingErrorText.FromExternalDetail(
                ex.FileName,
                "Asset UI non trovati nel percorso di build previsto.");
            ShowStartupError(
                "Interfaccia non trovata",
                $"La build statica della UI non e disponibile. Esegui lo script scripts\\Build-Web.ps1 prima di compilare o distribuire l'app.\n\nDettaglio: {detail}");
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            string detail = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Dettagli tecnici disponibili nei log locali.");
            ShowStartupError(
                "OnlyRag non puo avviare la UI",
                $"Si e verificato un errore durante l'inizializzazione della finestra principale.\n\nDettaglio: {detail}");
        }
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

}

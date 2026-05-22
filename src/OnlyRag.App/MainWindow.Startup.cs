using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using OnlyRag.Core;

namespace OnlyRag.App;

public partial class MainWindow
{
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
}

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using OnlyRag.Api;

namespace OnlyRag.App;

public partial class MainWindow
{
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
            string detail = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Dettagli tecnici disponibili nei log locali.");
            MessageBox.Show(
                this,
                $"OnlyRag non e riuscito a preparare l'uscita.\n\nDettaglio: {detail}",
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
        if (backendSettings is null || !backendSettings.IsRunning || string.IsNullOrWhiteSpace(backendSettings.BaseUrl))
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
            return "OnlyRag chiudera l'applicazione e terminera le altre istanze aperte della stessa installazione.\n\nUscire?";
        }

        List<string> lines = [];
        if (exitState.Reasons.Count > 0)
        {
            lines.Add("Sono presenti modifiche o attivita in corso:");
            lines.AddRange(exitState.Reasons.Select(reason => $"- {reason}"));
            lines.Add(string.Empty);
        }

        lines.Add("OnlyRag salvera il lavoro disponibile, cancellera i job locali attivi e terminera le altre istanze aperte della stessa installazione.");
        lines.Add(string.Empty);
        lines.Add("Uscire?");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<AppShutdownPreparationResponse?> TryPrepareBackendShutdownAsync()
    {
        if (backendSettings is null || !backendSettings.IsRunning || string.IsNullOrWhiteSpace(backendSettings.BaseUrl))
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

    private void AddBackendSessionToken(HttpClient httpClient)
    {
        if (backendSettings is not null && !string.IsNullOrWhiteSpace(backendSettings.ApiToken))
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

    private sealed class AppShutdownPreparationResponse
    {
        public bool IsComplete { get; init; }

        public List<string> UnstoppedJobIds { get; init; } = [];
    }
}

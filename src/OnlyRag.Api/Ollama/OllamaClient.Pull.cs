using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Api.Ollama;

internal sealed partial class OllamaClient
{
    public async Task PullModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);

        await SendAsync<PullResponse>(
            HttpMethod.Post,
            context,
            "api/pull",
            new
            {
                model = normalizedModelName,
                stream = false
            },
            cancellationToken);
    }

    public async Task PullModelAsync(
        string modelName,
        Func<OllamaModelPullProgress, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onProgress);

        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(context.BaseUri, "api/pull"))
        {
            Content = JsonContent.Create(new
            {
                model = normalizedModelName,
                stream = true
            })
        };

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(context.Timeout);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                string error = await ReadErrorMessageAsync(response, timeoutSource.Token);
                throw CreateApiException(response.StatusCode, error);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            bool sawDone = false;
            while (true)
            {
                string? line = await reader.ReadLineAsync(timeoutSource.Token);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                PullResponse? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<PullResponse>(line, JsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new OllamaApiException(
                        OllamaErrorKind.UnexpectedResponse,
                        "Ollama ha restituito avanzamento installazione modello non valido.",
                        innerException: ex);
                }

                if (chunk is null || string.IsNullOrWhiteSpace(chunk.Status))
                {
                    throw new OllamaApiException(
                        OllamaErrorKind.UnexpectedResponse,
                        "Ollama ha restituito avanzamento installazione modello vuoto.");
                }

                int? percent = ComputePullProgressPercent(chunk);
                await onProgress(
                    new OllamaModelPullProgress(
                        chunk.Status,
                        chunk.Total,
                        chunk.Completed,
                        percent,
                        chunk.Digest,
                        chunk.Layer),
                    timeoutSource.Token);
                sawDone = sawDone || string.Equals(chunk.Status, "success", StringComparison.OrdinalIgnoreCase);
            }

            if (!sawDone)
            {
                await onProgress(new OllamaModelPullProgress("Installazione modello completata", null, null, 100), timeoutSource.Token);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaApiException(
                OllamaErrorKind.Timeout,
                $"Ollama non ha risposto entro {context.Timeout.TotalSeconds:0} secondi.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaApiException(
                OllamaErrorKind.Unreachable,
                $"Non riesco a raggiungere Ollama su {context.BaseUri}. Controlla l'indirizzo e verifica che il servizio sia in esecuzione.",
                innerException: ex);
        }
    }

    private static int? ComputePullProgressPercent(PullResponse chunk)
    {
        if (chunk.Total is not > 0 || chunk.Completed is not >= 0)
        {
            return string.Equals(chunk.Status, "success", StringComparison.OrdinalIgnoreCase)
                ? 100
                : null;
        }

        return (int)Math.Clamp(Math.Round(chunk.Completed.Value * 100d / chunk.Total.Value), 0d, 100d);
    }
}

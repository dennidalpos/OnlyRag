using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class OllamaModelPullJobHandler : ILocalJobHandler
{
    public const string JobType = "ollama-model-pull";

    private readonly IOllamaClient ollamaClient;

    public OllamaModelPullJobHandler(IOllamaClient ollamaClient)
    {
        this.ollamaClient = ollamaClient;
    }

    public string Type => JobType;

    public async Task ExecuteAsync(LocalJob job, ILocalJobQueue queue, CancellationToken cancellationToken)
    {
        OllamaModelPullJobPayload? payload = JsonSerializer.Deserialize<OllamaModelPullJobPayload>(job.PayloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.ModelName))
        {
            await queue.FailAsync(job.Id, "Payload installazione modello non valido.", retryable: false, cancellationToken);
            return;
        }

        string model = OllamaSettingsService.NormalizeRequiredModelName(payload.ModelName);
        try
        {
            await queue.SaveCheckpointAsync(
                job.Id,
                new LocalJobCheckpoint(0, $"Installazione modello {model}", JsonSerializer.Serialize(payload)),
                cancellationToken);

            int lastProgressPercent = job.ProgressPercent;
            OllamaModelPullProgress? latestProgress = null;
            await ollamaClient.PullModelAsync(
                model,
                async (progress, ct) =>
                {
                    latestProgress = progress;
                    int progressPercent = progress.ProgressPercent ?? lastProgressPercent;
                    lastProgressPercent = progressPercent;
                    string step = string.IsNullOrWhiteSpace(progress.Status)
                        ? $"Installazione modello {model}"
                        : progress.Status;
                    await queue.SaveCheckpointAsync(
                        job.Id,
                        new LocalJobCheckpoint(progressPercent, step, JsonSerializer.Serialize(progress)),
                        ct);
                },
                cancellationToken);

            await queue.SaveCheckpointAsync(
                job.Id,
                new LocalJobCheckpoint(
                    100,
                    $"Modello {model} installato",
                    JsonSerializer.Serialize(latestProgress ?? new OllamaModelPullProgress("success", null, null, 100))),
                cancellationToken);
        }
        catch (OllamaApiException ex)
        {
            await queue.FailAsync(job.Id, ex.Message, IsRetryable(ex), cancellationToken);
        }
    }

    private static bool IsRetryable(OllamaApiException exception)
    {
        return exception.Kind is OllamaErrorKind.Timeout
            or OllamaErrorKind.Unreachable
            or OllamaErrorKind.UnexpectedResponse;
    }
}

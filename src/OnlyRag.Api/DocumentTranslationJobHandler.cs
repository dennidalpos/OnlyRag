using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class DocumentTranslationJobHandler : ILocalJobHandler
{
    public const string DocumentTranslationJobType = "document-translation";

    private readonly ITranslationRepository translations;
    private readonly IOllamaClient ollamaClient;
    private readonly IPerformanceSettingsService performanceSettings;
    private readonly IOllamaSettingsService settingsService;

    public DocumentTranslationJobHandler(
        ITranslationRepository translations,
        IOllamaClient ollamaClient,
        IPerformanceSettingsService performanceSettings,
        IOllamaSettingsService settingsService)
    {
        this.translations = translations;
        this.ollamaClient = ollamaClient;
        this.performanceSettings = performanceSettings;
        this.settingsService = settingsService;
    }

    public string Type => DocumentTranslationJobType;

    public async Task ExecuteAsync(LocalJob job, ILocalJobQueue queue, CancellationToken cancellationToken)
    {
        DocumentTranslationJobPayload? payload = JsonSerializer.Deserialize<DocumentTranslationJobPayload>(job.PayloadJson);
        if (payload is null || payload.TranslationId <= 0 || payload.DocumentId <= 0 || string.IsNullOrWhiteSpace(payload.Model))
        {
            await queue.FailAsync(job.Id, "Payload job traduzione non valido.", retryable: false, cancellationToken);
            return;
        }

        StoredTranslation? translation = await translations.GetAsync(payload.TranslationId, cancellationToken);
        if (translation is null)
        {
            await queue.CancelAsync(job.Id, cancellationToken);
            return;
        }

        string model = OllamaSettingsService.NormalizeRequiredModelName(payload.Model);
        DocumentTranslationPromptBuilder.NormalizeLanguage(payload.TargetLanguage);
        int batchSize = (await performanceSettings.GetAsync(cancellationToken)).TranslationBatchSize;
        int? translationNumCtx = (await settingsService.GetAsync(cancellationToken)).TranslationNumCtx;
        TranslationCheckpoint checkpoint = ReadCheckpoint(job.CheckpointJson, payload.TranslationId, model);

        try
        {
            await EnsureModelIsInstalledAsync(model, cancellationToken);
            await translations.UpdateTranslationJobAsync(payload.TranslationId, job.Id, "Running", null, cancellationToken);
            await TranslateFromCheckpointAsync(payload, model, batchSize, translationNumCtx, checkpoint, job, queue, cancellationToken);
            await translations.RefreshProgressAsync(payload.TranslationId, "Completed", null, cancellationToken);
        }
        catch (OllamaApiException ex)
        {
            bool retryable = ex.Kind is OllamaErrorKind.Timeout
                or OllamaErrorKind.Unreachable
                or OllamaErrorKind.UnexpectedResponse;
            LocalJob? failed = await queue.FailAsync(job.Id, ex.Message, retryable, cancellationToken);
            await translations.RefreshProgressAsync(
                payload.TranslationId,
                failed?.Status is JobStatus.Failed ? "Failed" : "Queued",
                ex.Message,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            string message = UserFacingErrorText.FromExternalDetail(
                ex.Message,
                "Traduzione non completata. Dettagli tecnici disponibili nei log locali.");
            LocalJob? failed = await queue.FailAsync(job.Id, message, retryable: true, cancellationToken);
            await translations.RefreshProgressAsync(
                payload.TranslationId,
                failed?.Status is JobStatus.Failed ? "Failed" : "Queued",
                message,
                cancellationToken);
        }
        catch (TranslationValidationException ex)
        {
            await translations.RefreshProgressAsync(payload.TranslationId, "Failed", ex.Message, cancellationToken);
            await queue.FailAsync(job.Id, ex.Message, retryable: false, cancellationToken);
        }
    }

    private async Task TranslateFromCheckpointAsync(
        DocumentTranslationJobPayload payload,
        string model,
        int batchSize,
        int? translationNumCtx,
        TranslationCheckpoint checkpoint,
        LocalJob job,
        ILocalJobQueue queue,
        CancellationToken cancellationToken)
    {
        int nextUnitIndex = checkpoint.NextUnitIndex;
        while (true)
        {
            StoredTranslation? current = await translations.GetAsync(payload.TranslationId, cancellationToken);
            if (current is null)
            {
                await queue.CancelAsync(job.Id, cancellationToken);
                return;
            }

            bool completedBatch = false;
            for (int batchIndex = 0; batchIndex < batchSize; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StoredTranslationUnit? unit = await translations.GetNextPendingUnitAsync(
                    payload.TranslationId,
                    nextUnitIndex,
                    cancellationToken);
                if (unit is null && nextUnitIndex > 0 && current.CompletedUnitCount < current.UnitCount)
                {
                    nextUnitIndex = 0;
                    continue;
                }

                if (unit is null)
                {
                    await SaveCheckpointAsync(job, queue, current, int.MaxValue, "completed", cancellationToken);
                    return;
                }

                IReadOnlyList<OllamaChatMessage> messages = DocumentTranslationPromptBuilder.BuildMessages(
                    payload.TargetLanguage,
                    unit);
                string translatedText = StripDelimiters(await ollamaClient.GenerateChatAsync(model, messages, translationNumCtx, cancellationToken));
                TranslationValidationResult validation = TranslationOutputValidator.Validate(unit.SourceText, translatedText);
                if (!validation.IsValid)
                {
                    await translations.SaveUnitFailureAsync(unit.Id, validation.Warnings ?? "Validazione traduzione fallita.", cancellationToken);
                    throw new InvalidOperationException(validation.Warnings ?? "Validazione traduzione fallita.");
                }

                await translations.SaveUnitSuccessAsync(unit.Id, translatedText.Trim(), validation.Warnings, cancellationToken);
                await translations.RefreshProgressAsync(payload.TranslationId, "Running", null, cancellationToken);
                current = await translations.GetAsync(payload.TranslationId, cancellationToken) ?? current;
                nextUnitIndex = unit.UnitIndex + 1;
                await SaveCheckpointAsync(job, queue, current, nextUnitIndex, "running", cancellationToken);
                completedBatch = true;
            }

            if (!completedBatch)
            {
                await SaveCheckpointAsync(job, queue, current, int.MaxValue, "completed", cancellationToken);
                return;
            }
        }
    }

    private async Task SaveCheckpointAsync(
        LocalJob job,
        ILocalJobQueue queue,
        StoredTranslation translation,
        int nextUnitIndex,
        string mode,
        CancellationToken cancellationToken)
    {
        int progressPercent = translation.UnitCount == 0
            ? 100
            : (int)Math.Round(translation.CompletedUnitCount * 100d / translation.UnitCount);
        if (mode == "completed")
        {
            progressPercent = 100;
        }

        await queue.SaveCheckpointAsync(
            job.Id,
            new LocalJobCheckpoint(
                Math.Clamp(progressPercent, 0, 100),
                mode == "completed"
                    ? $"Traduzione completata: {translation.CompletedUnitCount}/{translation.UnitCount} unita"
                    : $"Traduzione unita {translation.CompletedUnitCount}/{translation.UnitCount}",
                JsonSerializer.Serialize(new TranslationCheckpoint(
                    Version: 1,
                    translation.Id,
                    translation.Model,
                    nextUnitIndex,
                    translation.CompletedUnitCount,
                    mode))),
            cancellationToken);
    }

    private async Task EnsureModelIsInstalledAsync(string model, CancellationToken cancellationToken)
    {
        IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
        bool installed = models.Any(installedModel =>
            string.Equals(installedModel.Name, model, StringComparison.OrdinalIgnoreCase)
            || string.Equals(installedModel.Model, model, StringComparison.OrdinalIgnoreCase));
        if (!installed)
        {
            throw new OllamaApiException(
                OllamaErrorKind.ModelNotFound,
                $"Il modello traduzione '{model}' non e installato in Ollama.");
        }
    }

    private static TranslationCheckpoint ReadCheckpoint(
        string checkpointJson,
        long translationId,
        string model)
    {
        if (!string.IsNullOrWhiteSpace(checkpointJson))
        {
            try
            {
                TranslationCheckpoint? checkpoint = JsonSerializer.Deserialize<TranslationCheckpoint>(checkpointJson);
                if (checkpoint is not null
                    && checkpoint.TranslationId == translationId
                    && string.Equals(checkpoint.Model, model, StringComparison.OrdinalIgnoreCase))
                {
                    return checkpoint;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new TranslationCheckpoint(1, translationId, model, 0, 0, "new");
    }

    private sealed record TranslationCheckpoint(
        int Version,
        long TranslationId,
        string Model,
        int NextUnitIndex,
        int CompletedUnitCount,
        string Mode);

    private static string StripDelimiters(string text)
    {
        if (!text.Contains("ONLYRAG_TRANSLATION_UNIT", StringComparison.Ordinal)
            && !text.Contains("<source_text>", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("</source_text>", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        IEnumerable<string> lines = text.Split('\n')
            .Where(line =>
            {
                string trimmed = line.Trim();
                return !trimmed.Contains("ONLYRAG_TRANSLATION_UNIT", StringComparison.Ordinal)
                    && !string.Equals(trimmed, "<source_text>", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(trimmed, "</source_text>", StringComparison.OrdinalIgnoreCase);
            });
        return string.Join('\n', lines).Trim();
    }
}

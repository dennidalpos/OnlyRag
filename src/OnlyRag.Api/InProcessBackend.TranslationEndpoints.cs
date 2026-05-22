using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapTranslationEndpoints(WebApplication app)
    {
        app.MapPost("/api/translations", async (
            CreateTranslationRequest request,
            IDocumentLibraryService documents,
            ITranslationRepository translations,
            IOllamaClient ollamaClient,
            ILocalJobQueue jobs,
            CancellationToken cancellationToken) =>
        {
            try
            {
                ImportedDocument? document = await documents.GetAsync(request.DocumentId, cancellationToken);
                if (document is null)
                {
                    return CreateNotFoundProblem("Documento");
                }

                if (document.PageCount == 0)
                {
                    return CreateConflictProblem(
                        "Documento non indicizzato",
                        "Esegui prima l'ingestion del documento: la traduzione usa le unita testuali indicizzate.",
                        "document_not_indexed");
                }

                string model = OllamaSettingsService.NormalizeRequiredModelName(request.Model);
                string targetLanguage = DocumentTranslationPromptBuilder.NormalizeLanguage(request.TargetLanguage);
                await EnsureOllamaModelInstalledAsync(ollamaClient, model, "traduzione", cancellationToken);

                IReadOnlyList<TranslationSourceUnit> units = await translations.BuildSourceUnitsAsync(
                    request.DocumentId,
                    cancellationToken);
                StoredTranslation translation = await translations.CreateAsync(
                    request.DocumentId,
                    targetLanguage,
                    model,
                    jobId: null,
                    units,
                    cancellationToken);

                string payloadJson = System.Text.Json.JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                    translation.Id,
                    request.DocumentId,
                    targetLanguage,
                    model));
                LocalJob job = await jobs.CreateAsync(
                    new CreateLocalJobRequest(
                        DocumentTranslationJobHandler.DocumentTranslationJobType,
                        payloadJson,
                        Priority: 20,
                        MaxRetries: 2),
                    cancellationToken);
                await translations.UpdateTranslationJobAsync(translation.Id, job.Id, "Queued", null, cancellationToken);

                return Results.Ok(await BuildTranslationDetailAsync(translation.Id, translations, cancellationToken));
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex);
            }
            catch (TranslationValidationException ex)
            {
                return CreateBadRequestProblem(ex.Title, ex.Message, "translation_validation_failed");
            }
            catch (InvalidOperationException ex)
            {
                return CreateConflictProblem(
                    "Traduzione non avviata",
                    ex.Message,
                    "translation_not_started");
            }
        });

        app.MapGet("/api/translations/{id:long}", async (
            long id,
            ITranslationRepository translations,
            CancellationToken cancellationToken) =>
        {
            TranslationDetailResponse? detail = await BuildTranslationDetailAsync(id, translations, cancellationToken);
            return detail is null ? CreateNotFoundProblem("Traduzione") : Results.Ok(detail);
        });

        app.MapGet("/api/translations/{id:long}/compare", async (
            long id,
            int? page,
            ITranslationRepository translations,
            CancellationToken cancellationToken) =>
        {
            TranslationCompareResponse? compare = await BuildTranslationCompareAsync(
                id,
                page,
                translations,
                cancellationToken);
            return compare is null ? CreateNotFoundProblem("Traduzione") : Results.Ok(compare);
        });

        app.MapGet("/api/documents/{id:long}/translations", async (
            long id,
            IDocumentLibraryService documents,
            ITranslationRepository translations,
            CancellationToken cancellationToken) =>
        {
            ImportedDocument? document = await documents.GetAsync(id, cancellationToken);
            if (document is null)
            {
                return CreateNotFoundProblem("Documento");
            }

            IReadOnlyList<StoredTranslation> items = await translations.ListByDocumentAsync(id, cancellationToken);
            return Results.Ok(items.Select(item => item.ToResponse()).ToArray());
        });

        app.MapPut("/api/translations/{id:long}/units/{unitId:long}", async (
            long id,
            long unitId,
            UpdateTranslationUnitRequest request,
            ITranslationRepository translations,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TranslatedText))
            {
                return CreateBadRequestProblem(
                    "Correzione non valida",
                    "Il testo corretto non puo essere vuoto.",
                    "translation_unit_text_required");
            }

            StoredTranslationUnit? unit = await translations.UpdateUnitTextAsync(
                id,
                unitId,
                request.TranslatedText,
                cancellationToken);
            return unit is null ? CreateNotFoundProblem("Unita traduzione") : Results.Ok(unit.ToResponse());
        });

        app.MapPost("/api/translations/{id:long}/export", async (
            long id,
            TranslationExportRequest request,
            TranslationExportService exporter,
            CancellationToken cancellationToken) =>
        {
            try
            {
                TranslationExportResponse? response = await exporter.ExportAsync(id, request, cancellationToken);
                return response is null ? CreateNotFoundProblem("Traduzione") : Results.Ok(response);
            }
            catch (TranslationExportException ex)
            {
                return CreateBadRequestProblem(ex.Title, ex.Message, "translation_export_failed");
            }
        });
    }
}

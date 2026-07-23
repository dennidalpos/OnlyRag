using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapTranslationEndpoints(WebApplication app)
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
                        Priority: 20),
                    cancellationToken);
                await translations.UpdateTranslationJobAsync(translation.Id, job.Id, "Queued", null, cancellationToken);

                return Results.Ok(await BuildTranslationDetailAsync(translation.Id, translations, cancellationToken));
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex, app.Services, "/api/translations");
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

    private static async Task<TranslationDetailResponse?> BuildTranslationDetailAsync(
        long translationId,
        ITranslationRepository translations,
        CancellationToken cancellationToken)
    {
        StoredTranslation? translation = await translations.GetAsync(translationId, cancellationToken);
        if (translation is null)
        {
            return null;
        }

        IReadOnlyList<StoredTranslationUnit> units = await translations.ListUnitsPreviewAsync(
            translationId,
            take: 80,
            cancellationToken);
        return new TranslationDetailResponse(
            translation.ToResponse(),
            units.Select(unit => unit.ToResponse()).ToArray());
    }

    private static async Task<TranslationCompareResponse?> BuildTranslationCompareAsync(
        long translationId,
        int? requestedPage,
        ITranslationRepository translations,
        CancellationToken cancellationToken)
    {
        StoredTranslation? translation = await translations.GetAsync(translationId, cancellationToken);
        if (translation is null)
        {
            return null;
        }

        int[] pages = (await translations.ListUnitPagesAsync(
            translationId,
            cancellationToken)).ToArray();
        if (pages.Length == 0)
        {
            return new TranslationCompareResponse(
                translation.ToResponse(),
                CurrentPage: 1,
                PagePosition: 0,
                PageCount: 0,
                PreviousPage: null,
                NextPage: null,
                Units: []);
        }

        int currentPage = requestedPage.HasValue && pages.Contains(requestedPage.Value)
            ? requestedPage.Value
            : pages[0];
        int pageIndex = Array.IndexOf(pages, currentPage);
        IReadOnlyList<StoredTranslationUnit> pageUnits = await translations.ListUnitsByPageAsync(
            translationId,
            currentPage,
            cancellationToken);
        TranslationUnitResponse[] unitResponses = pageUnits
            .Select(unit => unit.ToResponse())
            .ToArray();

        return new TranslationCompareResponse(
            translation.ToResponse(),
            currentPage,
            pageIndex + 1,
            pages.Length,
            pageIndex > 0 ? pages[pageIndex - 1] : null,
            pageIndex < pages.Length - 1 ? pages[pageIndex + 1] : null,
            unitResponses);
    }
}

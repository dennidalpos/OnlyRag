using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapDocumentImportEndpoints(WebApplication app)
    {
        app.MapPost("/api/documents/import", async (
            HttpRequest request,
            IDocumentLibraryService documents,
            LocalDocumentStorageGuard storageGuard,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return CreateBadRequestProblem(
                    "Richiesta import non valida",
                    "Usa multipart/form-data con uno o piu file.",
                    "document_import_invalid_content_type");
            }

            if (request.ContentLength > storageGuard.Limits.MaxRequestBodySizeBytes)
            {
                return CreateProblem(
                    "Import troppo grande",
                    $"La richiesta supera il limite di {LocalDocumentLibraryLimits.FormatBytes(storageGuard.Limits.MaxRequestBodySizeBytes)}.",
                    StatusCodes.Status413PayloadTooLarge,
                    "document_import_request_too_large");
            }

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (BadHttpRequestException)
            {
                return CreateRequestTooLargeProblem();
            }
            catch (InvalidDataException)
            {
                return CreateRequestTooLargeProblem();
            }

            if (form.Files.Count == 0)
            {
                return CreateBadRequestProblem(
                    "Nessun file selezionato",
                    "Seleziona almeno un file da importare.",
                    "document_import_files_required");
            }

            try
            {
                ValidateImportBatch(form.Files, storageGuard);
            }
            catch (ArgumentException ex)
            {
                return CreateBadRequestProblem(
                    "Import non valido",
                    ex.Message,
                    "document_import_invalid");
            }
            catch (DocumentStorageLimitException ex)
            {
                return MapDocumentStorageLimitException(ex);
            }

            bool forceOcr = string.Equals(
                form["ocrPolicy"].ToString(),
                "ForceAll",
                StringComparison.OrdinalIgnoreCase);
            string ocrLanguage = OcrLanguages.NormalizeCode(form["ocrLanguage"].ToString());

            List<DocumentImportFileResult> results = [];
            foreach (IFormFile file in form.Files)
            {
                results.Add(await ImportSingleFileAsync(
                    documents,
                    file,
                    forceOcr,
                    ocrLanguage,
                    cancellationToken));
            }

            return Results.Ok(new DocumentImportResponse(
                results.Where(result => result.Succeeded && result.Document is not null)
                    .Select(result => new DocumentImportResult(result.Document!, result.Deduplicated, result.Message))
                    .ToArray(),
                results,
                results.Any(result => !result.Succeeded)));
        });
    }

    private static IResult CreateRequestTooLargeProblem()
    {
        return CreateProblem(
            "Import troppo grande",
            "La richiesta multipart supera i limiti configurati.",
            StatusCodes.Status413PayloadTooLarge,
            "document_import_request_too_large");
    }

    private static async Task<DocumentImportFileResult> ImportSingleFileAsync(
        IDocumentLibraryService documents,
        IFormFile file,
        bool forceOcr,
        string ocrLanguage,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            return DocumentImportFileResult.Failed(
                file.FileName,
                "Il file e vuoto.",
                "document_import_empty_file");
        }

        try
        {
            await using Stream stream = file.OpenReadStream();
            DocumentImportResult imported = await documents.ImportAsync(
                stream,
                file.FileName,
                forceOcr,
                ocrLanguage,
                cancellationToken);
            return DocumentImportFileResult.Imported(file.FileName, imported);
        }
        catch (ArgumentException ex)
        {
            return DocumentImportFileResult.Failed(file.FileName, ex.Message, "document_import_invalid");
        }
        catch (DocumentStorageLimitException ex)
        {
            return DocumentImportFileResult.Failed(file.FileName, ex.Message, "document_storage_limit");
        }
        catch (InvalidOperationException)
        {
            return DocumentImportFileResult.Failed(
                file.FileName,
                "Il file non puo essere salvato nella libreria locale.",
                "document_import_invalid_path");
        }
    }
}

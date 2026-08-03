using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static class InProcessBackendExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/export").WithTags("Export");

        group.MapPost("/preview", async (
            ExportPreviewRequest request,
            IChatReportExportService exportService,
            CancellationToken ct) =>
        {
            try
            {
                ExportPreviewResponse preview = await exportService.GeneratePreviewAsync(request, ct);
                return Results.Ok(preview);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { title = "Dati anteprima non validi", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "Errore durante la generazione dell'anteprime", detail: ex.Message);
            }
        });

        group.MapPost("/chat", async (
            ExportChatReportRequest request,
            IChatReportExportService exportService,
            CancellationToken ct) =>
        {
            try
            {
                ExportReportResult result = await exportService.ExportReportAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { title = "Dati export non validi", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "Errore durante l'esportazione", detail: ex.Message);
            }
        });

        group.MapPost("/retrieval", async (
            ExportChatReportRequest request,
            IChatReportExportService exportService,
            CancellationToken ct) =>
        {
            try
            {
                ExportReportResult result = await exportService.ExportReportAsync(request, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "Errore esportazione report RAG", detail: ex.Message);
            }
        });

        return endpoints;
    }
}

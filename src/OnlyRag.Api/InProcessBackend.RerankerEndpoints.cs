using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapRerankerEndpoints(this WebApplication app)
    {
        app.MapGet("/api/rag/reranker/model", async (
            RerankerModelManager rerankerModelManager,
            CancellationToken cancellationToken) =>
        {
            RerankerModelInfo status = await rerankerModelManager.GetModelStatusAsync(cancellationToken);
            return Results.Ok(status);
        });

        app.MapPost("/api/rag/reranker/download", async (
            RerankerModelManager rerankerModelManager,
            CancellationToken cancellationToken) =>
        {
            try
            {
                bool result = await rerankerModelManager.DownloadModelAsync(null, cancellationToken);
                return Results.Ok(new { Success = result });
            }
            catch (InvalidOperationException ex)
            {
                return CreateBadRequestProblem("Download già in corso", ex.Message, "reranker_download_in_progress");
            }
            catch (Exception ex)
            {
                return CreateBadRequestProblem("Errore download modello ONNX", ex.Message, "reranker_download_error");
            }
        });

        app.MapDelete("/api/rag/reranker/download", async (
            RerankerModelManager rerankerModelManager) =>
        {
            await rerankerModelManager.CancelDownloadAsync();
            return Results.Ok(new { Success = true });
        });

        app.MapDelete("/api/rag/reranker/model", async (
            RerankerModelManager rerankerModelManager,
            CancellationToken cancellationToken) =>
        {
            bool deleted = await rerankerModelManager.DeleteModelAsync(cancellationToken);
            return Results.Ok(new { Deleted = deleted });
        });
    }
}

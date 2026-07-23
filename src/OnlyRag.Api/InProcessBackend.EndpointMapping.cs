using Microsoft.AspNetCore.Builder;

namespace OnlyRag.Api;

internal static class InProcessBackendEndpointMapping
{
    public static WebApplication MapOnlyRagFeatureEndpoints(this WebApplication app)
    {
        InProcessBackend.MapAppEndpoints(app);
        InProcessBackend.MapRetrievalEndpoints(app);
        InProcessBackend.MapSettingsEndpoints(app);
        InProcessBackend.MapDependencyEndpoints(app);
        InProcessBackend.MapJobEndpoints(app);
        InProcessBackend.MapDocumentEndpoints(app);
        InProcessBackend.MapTranslationEndpoints(app);
        InProcessBackend.MapImageEndpoints(app);
        InProcessBackend.MapCodingEndpoints(app);
        app.MapWorkspaceEndpoints();
        return app;
    }
}

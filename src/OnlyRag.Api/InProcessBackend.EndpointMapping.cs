using Microsoft.AspNetCore.Builder;

namespace OnlyRag.Api;

internal static class InProcessBackendEndpointMapping
{
    public static WebApplication MapOnlyRagFeatureEndpoints(this WebApplication app)
    {
        app.MapAppEndpoints();
        app.MapRetrievalEndpoints();
        app.MapSettingsEndpoints();
        app.MapDependencyEndpoints();
        app.MapJobEndpoints();
        app.MapDocumentEndpoints();
        app.MapTranslationEndpoints();
        app.MapImageEndpoints();
        app.MapCodingEndpoints();
        app.MapWorkspaceEndpoints();
        app.MapLoggingEndpoints();
        return app;
    }
}


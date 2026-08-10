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
        app.MapWorkspaceEndpoints();
        app.MapAgentEndpoints();
        app.MapExportEndpoints();
        app.MapUpdateEndpoints();
        app.MapCloudLlmEndpoints();
        app.MapDatabaseEndpoints();
        app.MapGraphEndpoints();
        app.MapLoggingEndpoints();
        return app;
    }

}

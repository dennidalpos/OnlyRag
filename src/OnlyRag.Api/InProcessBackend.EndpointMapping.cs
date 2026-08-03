using Microsoft.AspNetCore.Builder;

namespace OnlyRag.Api;

internal static class InProcessBackendEndpointMapping
{
    public static WebApplication MapOnlyRagFeatureEndpoints(this WebApplication app)
    {
        app.MapAppEndpoints();
        app.MapRetrievalEndpoints();
        app.MapRerankerEndpoints();
        app.MapSettingsEndpoints();
        app.MapDependencyEndpoints();
        app.MapJobEndpoints();
        app.MapDocumentEndpoints();
        app.MapTranslationEndpoints();
        app.MapImageEndpoints();
        app.MapWorkspaceEndpoints();
        app.MapAgentEndpoints();
        app.MapMultiAgentEndpoints();
        app.MapMcpEndpoints();
        app.MapExportEndpoints();
        app.MapBackupEndpoints();
        app.MapDatabaseEndpoints();
        app.MapLoggingEndpoints();
        return app;
    }
}

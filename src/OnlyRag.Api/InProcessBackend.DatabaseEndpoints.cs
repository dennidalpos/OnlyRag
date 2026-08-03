using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapDatabaseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/database/status", async (
            ISqliteMaintenanceService maintenanceService,
            CancellationToken cancellationToken) =>
        {
            var status = await maintenanceService.GetStatusAsync(cancellationToken);
            return Results.Ok(status);
        });

        app.MapPost("/api/system/database/maintenance", async (
            ISqliteMaintenanceService maintenanceService,
            CancellationToken cancellationToken) =>
        {
            var result = await maintenanceService.RunMaintenanceAsync(cancellationToken);
            return Results.Ok(result);
        });
    }
}

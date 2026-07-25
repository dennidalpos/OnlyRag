using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapLoggingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/logging", async (
            ILoggingService loggingService,
            CancellationToken cancellationToken) =>
        {
            var settings = await loggingService.GetSettingsAsync(cancellationToken);
            return Results.Ok(settings);
        });

        app.MapPost("/api/settings/logging", async (
            LoggingSettings settings,
            ILoggingService loggingService,
            CancellationToken cancellationToken) =>
        {
            await loggingService.UpdateSettingsAsync(settings, cancellationToken);
            return Results.Ok(settings);
        });

        app.MapGet("/api/logs", (
            ILoggingService loggingService,
            AppLogLevel? minLevel,
            string? search,
            int? limit) =>
        {
            var logs = loggingService.GetRecentLogs(minLevel, search, limit ?? 200);
            return Results.Ok(logs);
        });

        app.MapGet("/api/logs/storage", (ILoggingService loggingService) =>
        {
            var storageInfo = loggingService.GetStorageInfo();
            return Results.Ok(storageInfo);
        });

        app.MapDelete("/api/logs", async (
            ILoggingService loggingService,
            CancellationToken cancellationToken) =>
        {
            await loggingService.ClearLogsAsync(cancellationToken);
            return Results.Ok(new { success = true, message = "Log cancellati ed azzerati con successo." });
        });
    }
}

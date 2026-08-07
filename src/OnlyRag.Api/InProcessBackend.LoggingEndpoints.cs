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
            string? minLevel,
            string? search,
            int? limit) =>
        {
            AppLogLevel? filterLevel = null;
            if (!string.IsNullOrWhiteSpace(minLevel) &&
                Enum.TryParse<AppLogLevel>(minLevel, ignoreCase: true, out var parsedLevel))
            {
                filterLevel = parsedLevel;
            }

            var logs = loggingService.GetRecentLogs(filterLevel, search, limit ?? 200);
            return Results.Ok(logs);
        });

        app.MapGet("/api/logs/stream", async (
            HttpContext httpContext,
            ILoggingService loggingService,
            string? minLevel,
            string? search) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            AppLogLevel? filterLevel = null;
            if (!string.IsNullOrWhiteSpace(minLevel) &&
                Enum.TryParse<AppLogLevel>(minLevel, ignoreCase: true, out var parsedLevel))
            {
                filterLevel = parsedLevel;
            }

            var channel = System.Threading.Channels.Channel.CreateUnbounded<LogEntry>();

            bool MatchesFilter(LogEntry entry)
            {
                if (filterLevel.HasValue && entry.Level < filterLevel.Value)
                {
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s = search.Trim();
                    return entry.Message.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                           entry.Category.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                           (entry.ExceptionDetails != null && entry.ExceptionDetails.Contains(s, StringComparison.OrdinalIgnoreCase));
                }
                return true;
            }

            void OnLog(LogEntry entry)
            {
                if (MatchesFilter(entry))
                {
                    channel.Writer.TryWrite(entry);
                }
            }

            LoggingService? concreteLogging = loggingService as LoggingService;
            if (concreteLogging is not null)
            {
                concreteLogging.OnLogWritten += OnLog;
            }

            try
            {
                var initialLogs = loggingService.GetRecentLogs(filterLevel, search, limit: 50);
                var reversed = initialLogs.Reverse().ToList();
                foreach (var log in reversed)
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(log, AgentJsonOptions);
                    await httpContext.Response.WriteAsync($"data: {json}\n\n", httpContext.RequestAborted);
                }
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);

                while (await channel.Reader.WaitToReadAsync(httpContext.RequestAborted))
                {
                    while (channel.Reader.TryRead(out var entry))
                    {
                        string json = System.Text.Json.JsonSerializer.Serialize(entry, AgentJsonOptions);
                        await httpContext.Response.WriteAsync($"data: {json}\n\n", httpContext.RequestAborted);
                    }
                    await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }
            finally
            {
                if (concreteLogging is not null)
                {
                    concreteLogging.OnLogWritten -= OnLog;
                }
            }

            return Results.Empty;
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

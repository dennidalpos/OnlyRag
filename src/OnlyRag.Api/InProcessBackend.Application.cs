using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private const string WebViewCorsPolicy = "OnlyRagWebView";

    private static WebApplication BuildApplication(
        InProcessBackendDescriptor descriptor,
        InProcessBackendOptions options,
        BackendRuntimeState runtimeState,
        string sessionToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(InProcessBackend).Assembly.GetName().Name,
            Args = []
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(options.Address, options.Port);
            kestrel.Limits.MaxRequestBodySize = options.DocumentLibraryLimits.MaxRequestBodySizeBytes;
        });

        builder.Services.AddOnlyRagBackendServices(descriptor, options, runtimeState);
        builder.Services.AddOnlyRagHttpApiOptions(
            options,
            WebViewCorsPolicy,
            ResolveAllowedCorsOrigins(options));

        WebApplication app = builder.Build();

        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                string correlationId = context.TraceIdentifier;
                if (exceptionFeature?.Error is Exception exception)
                {
                    if (IsClientAbortException(exception))
                    {
                        context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
                        return;
                    }

                    var appDescriptor = context.RequestServices.GetRequiredService<InProcessBackendDescriptor>();
                    string requestPath = $"{context.Request.Method} {context.Request.Path}";
                    BackendLog.WriteException(
                        appDescriptor.StoragePaths,
                        correlationId,
                        $"Unhandled API exception for {requestPath}.",
                        exception);
                }

                await WriteProblemAsync(
                    context,
                    "Errore interno del server.",
                    CreateUnexpectedErrorDetail(correlationId),
                    StatusCodes.Status500InternalServerError,
                    "unexpected_error",
                    correlationId);
            });
        });
        app.UseCors(WebViewCorsPolicy);
        UseSessionTokenAuthentication(app, sessionToken);
        app.MapOnlyRagFeatureEndpoints();

        return app;
    }

    private static bool IsClientAbortException(Exception exception)
    {
        return exception is OperationCanceledException
            || exception.GetType().FullName == "System.Net.Http.HttpIOException"
                && exception.Message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ResolveAllowedCorsOrigins(InProcessBackendOptions options)
    {
        List<string> origins = [OnlyRagWebOrigins.StaticWebViewOrigin];
        if (options.EnableDevelopmentCorsOrigins)
        {
            origins.Add("http://127.0.0.1:5173");
            origins.Add("http://localhost:5173");
        }

        return origins.ToArray();
    }

    private static Uri ResolveBaseUri(IHost app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();
        string? address = addresses?.Addresses.FirstOrDefault();

        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("The in-process backend started without a resolvable listening address.");
        }

        return uri;
    }
}

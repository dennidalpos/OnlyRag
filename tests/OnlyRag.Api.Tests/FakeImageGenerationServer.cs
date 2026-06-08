using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OnlyRag.Api.Tests;

internal sealed class FakeImageGenerationServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string PngBase64 =
        Convert.ToBase64String(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+P+/HgAFeAJ5QxM1GQAAAABJRU5ErkJggg=="));

    private readonly WebApplication app;

    private FakeImageGenerationServer(WebApplication app, string baseUrl)
    {
        this.app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public static async Task<FakeImageGenerationServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = []
        });
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();

        app.MapGet("/sdapi/v1/sd-models", () => Results.Json(new[] { new { title = "test-model" } }, JsonOptions));
        app.MapPost("/sdapi/v1/txt2img", async (HttpRequest request) =>
        {
            _ = await ReadBodyAsync(request);
            return Results.Json(new { images = new[] { PngBase64 } }, JsonOptions);
        });

        app.MapGet("/system_stats", () => Results.Json(new { system = new { os = "test" } }, JsonOptions));
        app.MapPost("/prompt", async (HttpRequest request) =>
        {
            _ = await ReadBodyAsync(request);
            return Results.Json(new { prompt_id = "prompt-1", number = 1 }, JsonOptions);
        });
        app.MapGet("/history/{promptId}", (string promptId) => Results.Json(new Dictionary<string, object>
        {
            [promptId] = new
            {
                outputs = new Dictionary<string, object>
                {
                    ["7"] = new
                    {
                        images = new[]
                        {
                            new { filename = "comfy.png", subfolder = "", type = "output" }
                        }
                    }
                }
            }
        }, JsonOptions));
        app.MapGet("/view", () => Results.File(Convert.FromBase64String(PngBase64), "image/png", "comfy.png"));

        await app.StartAsync();
        return new FakeImageGenerationServer(app, ResolveBaseUrl(app));
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
    }

    private static string ResolveBaseUrl(IHost host)
    {
        IServer server = host.Services.GetRequiredService<IServer>();
        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>();
        return addresses?.Addresses.Single()
            ?? throw new InvalidOperationException("Fake image generation server started without a resolved address.");
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using StreamReader reader = new(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}


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

internal sealed class FakeOllamaServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplication app;

    private FakeOllamaServer(
        WebApplication app,
        string baseUrl,
        List<string> deletedModels,
        List<string> shownModels)
    {
        this.app = app;
        BaseUrl = baseUrl;
        DeletedModels = deletedModels;
        ShownModels = shownModels;
    }

    public string BaseUrl { get; }

    public IReadOnlyList<string> DeletedModels { get; }

    public IReadOnlyList<string> ShownModels { get; }

    public static async Task<FakeOllamaServer> StartAsync()
    {
        List<string> deletedModels = [];
        List<string> shownModels = [];
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = []
        });
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();

        app.MapGet("/api/tags", () => Results.Json(TagsPayload(), JsonOptions));
        app.MapPost("/api/embed", async (HttpRequest request) =>
            Results.Json(EmbedPayload(await ReadBodyAsync(request)), JsonOptions));
        app.MapPost("/api/chat", async (HttpRequest request) =>
            Results.Json(ChatPayload(await ReadBodyAsync(request)), JsonOptions));
        app.MapDelete("/api/delete", async (HttpRequest request) =>
        {
            deletedModels.Add(ExtractModelName(await ReadBodyAsync(request)));
            return Results.Json(new { }, JsonOptions);
        });
        app.MapPost("/api/show", async (HttpRequest request) =>
        {
            shownModels.Add(ExtractModelName(await ReadBodyAsync(request)));
            return Results.Json(new
            {
                model_info = new Dictionary<string, int>
                {
                    ["llama.context_length"] = 4096
                }
            }, JsonOptions);
        });

        await app.StartAsync();
        return new FakeOllamaServer(app, ResolveBaseUrl(app), deletedModels, shownModels);
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
            ?? throw new InvalidOperationException("Fake Ollama server started without a resolved address.");
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using StreamReader reader = new(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static object TagsPayload()
    {
        return new
        {
            models = new[]
            {
                new { name = "chat-model", model = "chat-model", size = 1 },
                new { name = "embed-model", model = "embed-model", size = 1 },
                new { name = "translation-model", model = "translation-model", size = 1 }
            }
        };
    }

    private static object EmbedPayload(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement input = document.RootElement.GetProperty("input");
        string[] inputs = input.ValueKind == JsonValueKind.Array
            ? input.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [input.GetString() ?? string.Empty];
        return new { embeddings = inputs.Select(CreateEmbedding).ToArray() };
    }

    private static float[] CreateEmbedding(string input)
    {
        return input.Contains("ZETA-777", StringComparison.OrdinalIgnoreCase)
            || input.Contains("codice operativo", StringComparison.OrdinalIgnoreCase)
            ? [1f, 0f, 0f]
            : [0f, 1f, 0f];
    }

    private static object ChatPayload(string body)
    {
        string answer = body.Contains("ONLYRAG_TRANSLATION_UNIT", StringComparison.Ordinal)
            ? $"Translated: {ExtractTranslationSource(body)}"
            : "Il documento indica il protocollo ZETA-777.";
        return new
        {
            done = true,
            message = new { role = "assistant", content = answer }
        };
    }

    private static string ExtractModelName(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("model").GetString() ?? string.Empty;
    }

    private static string ExtractTranslationSource(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement messages = document.RootElement.GetProperty("messages");
        string prompt = messages.EnumerateArray().Last().GetProperty("content").GetString() ?? string.Empty;
        int firstLineEnd = prompt.IndexOf('\n');
        int lastMarker = prompt.LastIndexOf("ONLYRAG_TRANSLATION_UNIT", StringComparison.Ordinal);
        return firstLineEnd < 0 || lastMarker <= firstLineEnd
            ? prompt
            : prompt[(firstLineEnd + 1)..lastMarker].Trim();
    }
}

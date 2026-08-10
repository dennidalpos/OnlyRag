using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlyRag.Core;

namespace OnlyRag.Api.Ollama;

internal sealed partial class OllamaClient : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly IOllamaSettingsService settingsService;
    private readonly OllamaGenerationCoordinator generationCoordinator;

    public OllamaClient(
        HttpClient httpClient,
        IOllamaSettingsService settingsService,
        OllamaGenerationCoordinator generationCoordinator)
    {
        this.httpClient = httpClient;
        this.settingsService = settingsService;
        this.generationCoordinator = generationCoordinator;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await ListModelsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        OllamaTagsResponse response = await SendAsync<OllamaTagsResponse>(
            HttpMethod.Get,
            context,
            "api/tags",
            body: null,
            cancellationToken);

        return response.Models
            .Select(model => new OllamaModelSummary(
                model.Name,
                model.Model,
                model.ModifiedAt,
                model.Size,
                model.Digest,
                model.Details?.Family,
                model.Details?.ParameterSize,
                model.Details?.QuantizationLevel))
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        OllamaVersionResponse response = await SendAsync<OllamaVersionResponse>(
            HttpMethod.Get,
            context,
            "api/version",
            body: null,
            cancellationToken);

        return string.IsNullOrWhiteSpace(response.Version) ? null : response.Version.Trim();
    }

    public async Task<IReadOnlyList<OllamaRunningModelResponse>> ListRunningModelsAsync(CancellationToken cancellationToken = default)
    {
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        OllamaPsResponse response = await SendAsync<OllamaPsResponse>(
            HttpMethod.Get,
            context,
            "api/ps",
            body: null,
            cancellationToken);

        return response.Models
            .Select(model => new OllamaRunningModelResponse(
                model.Name,
                model.Model,
                model.Size,
                model.SizeVram,
                model.Digest,
                model.ContextLength ?? ExtractContextLength(model.ModelInfo)))
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);

        await SendAsync<JsonElement>(
            HttpMethod.Delete,
            context,
            "api/delete",
            new { model = normalizedModelName },
            cancellationToken);
    }

    public async Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default)
    {
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);

        OllamaChatResponse response = await generationCoordinator.RunAsync(
            ct => SendAsync<OllamaChatResponse>(
                HttpMethod.Post,
                context,
                "api/chat",
                new
                {
                    model = normalizedModelName,
                    stream = false,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = "ping"
                        }
                    }
                },
                ct),
            cancellationToken);

        if (!response.Done)
        {
            throw new OllamaApiException(
                OllamaErrorKind.UnexpectedResponse,
                "Ollama replied to the test chat with an incomplete result.");
        }
    }

    public async Task<string> GenerateChatAsync(
        string modelName,
        IReadOnlyList<OllamaChatMessage> messages,
        int? numCtx = null,
        object? format = null,
        object? tools = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);
        if (messages.Count == 0)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "The chat request must contain at least one message.");
        }

        if (messages.Any(message => string.IsNullOrWhiteSpace(message.Role) || string.IsNullOrWhiteSpace(message.Content)))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "Chat messages must include role and content.");
        }

        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        var payload = new Dictionary<string, object>
        {
            ["model"] = normalizedModelName,
            ["stream"] = false,
            ["messages"] = messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            })
        };
        if (numCtx.HasValue) payload["options"] = new { num_ctx = numCtx.Value };
        if (format is not null) payload["format"] = format;
        if (tools is not null) payload["tools"] = tools;

        OllamaChatResponse response = await generationCoordinator.RunAsync(
            ct => SendAsync<OllamaChatResponse>(
                HttpMethod.Post,
                context,
                "api/chat",
                payload,
                ct),
            cancellationToken);

        string content = response.Message?.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new OllamaApiException(
                OllamaErrorKind.UnexpectedResponse,
                "Ollama did not return text for the chat response.");
        }

        return content;
    }

    public async IAsyncEnumerable<string> GenerateChatStreamAsync(
        string modelName,
        IReadOnlyList<OllamaChatMessage> messages,
        int? numCtx = null,
        object? format = null,
        object? tools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);
        if (messages.Count == 0)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "The chat request must contain at least one message.");
        }

        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        using IDisposable generationLease = await generationCoordinator.AcquireAsync(cancellationToken);
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(context.Timeout);
        CancellationToken requestToken = timeoutSource.Token;
        var payload = new Dictionary<string, object>
        {
            ["model"] = normalizedModelName,
            ["stream"] = true,
            ["messages"] = messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            })
        };
        if (numCtx.HasValue) payload["options"] = new { num_ctx = numCtx.Value };
        if (format is not null) payload["format"] = format;
        if (tools is not null) payload["tools"] = tools;

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(context.BaseUri, "api/chat"))
        {
            Content = JsonContent.Create(payload)
        };

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            requestToken);
        response.EnsureSuccessStatusCode();

        using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(requestToken);
        using System.IO.StreamReader reader = new(stream);

        while (!requestToken.IsCancellationRequested && await reader.ReadLineAsync(requestToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaChatResponse? chunk = null;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk?.Message?.Content is { } contentChunk && contentChunk.Length > 0)
            {
                yield return contentChunk;
            }

            if (chunk?.Done == true) break;
        }
    }

    public async Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IReadOnlyList<float>> embeddings = await GenerateEmbeddingsAsync(
            modelName,
            ["ping"],
            cancellationToken: cancellationToken);

        if (embeddings.Count == 0 || embeddings[0].Count == 0)
        {
            throw new OllamaApiException(
                OllamaErrorKind.UnexpectedResponse,
                "Ollama did not return valid embeddings for the requested model.");
        }
    }

    public async Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);

        OllamaShowResponse response = await SendAsync<OllamaShowResponse>(
            HttpMethod.Post,
            context,
            "api/show",
            new { model = normalizedModelName, verbose = false },
            cancellationToken);

        int? numCtx = null;
        if (response.ModelInfo is { } info)
        {
            foreach (KeyValuePair<string, System.Text.Json.JsonElement> kv in info)
            {
                if (kv.Key.EndsWith(".context_length", StringComparison.OrdinalIgnoreCase)
                    && kv.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                    && kv.Value.TryGetInt32(out int val))
                {
                    numCtx = val;
                    break;
                }
            }
        }

        return new OllamaModelDetails(normalizedModelName, numCtx);
    }

    public async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
        string modelName,
        IReadOnlyList<string> inputs,
        int? numCtx = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);
        if (inputs.Count == 0)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "The embedding request must contain at least one chunk.");
        }

        if (inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "Empty chunks cannot be sent to Ollama for embeddings.");
        }

        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        object inputPayload = inputs.Count == 1 ? inputs[0] : inputs;
        object requestBody = numCtx.HasValue
            ? new { model = normalizedModelName, input = inputPayload, truncate = false, options = new { num_ctx = numCtx.Value } }
            : (object)new { model = normalizedModelName, input = inputPayload, truncate = false };

        EmbeddingResponse response = await generationCoordinator.RunAsync(
            ct => SendAsync<EmbeddingResponse>(
                HttpMethod.Post,
                context,
                "api/embed",
                requestBody,
                ct),
            cancellationToken);

        if (response.Embeddings.Count != inputs.Count)
        {
            throw new OllamaApiException(
                OllamaErrorKind.UnexpectedResponse,
                "Ollama returned a different number of embeddings than the number of chunks sent.");
        }

        return response.Embeddings;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        OllamaRequestContext context,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(context.BaseUri, relativePath));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(context.Timeout);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, timeoutSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                string error = await ReadErrorMessageAsync(response, timeoutSource.Token);
                throw CreateApiException(response.StatusCode, error);
            }

            if (typeof(TResponse) == typeof(JsonElement) || response.Content.Headers.ContentLength is 0)
            {
                return default!;
            }

            TResponse? payload = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, timeoutSource.Token);
            if (payload is null)
            {
                throw new OllamaApiException(
                    OllamaErrorKind.UnexpectedResponse,
                    "Ollama returned an empty response.");
            }

            return payload;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaApiException(
                OllamaErrorKind.Timeout,
                $"Ollama did not respond within {context.Timeout.TotalSeconds:0} seconds.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaApiException(
                OllamaErrorKind.Unreachable,
                $"Cannot reach Ollama at {context.BaseUri}. Check the address and ensure the service is running.",
                innerException: ex);
        }
    }

    private async Task<OllamaRequestContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        OllamaSettings settings = await settingsService.GetAsync(cancellationToken);
        string normalizedBaseUrl = OllamaSettingsService.NormalizeAndValidateBaseUrl(
            settings.OllamaBaseUrl,
            settings.TrustNonLocalEndpoint);

        return new OllamaRequestContext(
            new Uri($"{normalizedBaseUrl.TrimEnd('/')}/", UriKind.Absolute),
            TimeSpan.FromSeconds(OllamaSettingsService.ValidateRequestTimeoutSeconds(settings.RequestTimeoutSeconds)));
    }

    private static int? ExtractContextLength(IReadOnlyDictionary<string, System.Text.Json.JsonElement>? modelInfo)
    {
        if (modelInfo is null)
        {
            return null;
        }

        foreach (KeyValuePair<string, System.Text.Json.JsonElement> kv in modelInfo)
        {
            if (!kv.Key.EndsWith(".context_length", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(kv.Key, "context_length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (kv.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                && kv.Value.TryGetInt32(out int value))
            {
                return value;
            }
        }

        return null;
    }

}

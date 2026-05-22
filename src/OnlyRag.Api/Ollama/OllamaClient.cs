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

    public OllamaClient(HttpClient httpClient, IOllamaSettingsService settingsService)
    {
        this.httpClient = httpClient;
        this.settingsService = settingsService;
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

    public async Task PullModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);

        await SendAsync<PullResponse>(
            HttpMethod.Post,
            context,
            "api/pull",
            new
            {
                model = normalizedModelName,
                stream = false
            },
            cancellationToken);
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

        OllamaChatResponse response = await SendAsync<OllamaChatResponse>(
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
            cancellationToken);

        if (!response.Done)
        {
            throw new OllamaApiException(
                OllamaErrorKind.UnexpectedResponse,
                "Ollama ha risposto alla chat di prova con un risultato incompleto.");
        }
    }

    public async Task<string> GenerateChatAsync(
        string modelName,
        IReadOnlyList<OllamaChatMessage> messages,
        int? numCtx = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedModelName = OllamaSettingsService.NormalizeRequiredModelName(modelName);
        if (messages.Count == 0)
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "La richiesta chat deve contenere almeno un messaggio.");
        }

        if (messages.Any(message => string.IsNullOrWhiteSpace(message.Role) || string.IsNullOrWhiteSpace(message.Content)))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "I messaggi chat devono includere ruolo e contenuto.");
        }

        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        object requestBody = numCtx.HasValue
            ? new
            {
                model = normalizedModelName,
                stream = false,
                messages = messages.Select(message => new
                {
                    role = message.Role,
                    content = message.Content
                }),
                options = new { num_ctx = numCtx.Value }
            }
            : (object)new
            {
                model = normalizedModelName,
                stream = false,
                messages = messages.Select(message => new
                {
                    role = message.Role,
                    content = message.Content
                })
            };
        OllamaChatResponse response = await SendAsync<OllamaChatResponse>(
            HttpMethod.Post,
            context,
            "api/chat",
            requestBody,
            cancellationToken);

        string content = response.Message?.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new OllamaApiException(
                OllamaErrorKind.UnexpectedResponse,
                "Ollama non ha restituito testo per la risposta chat.");
        }

        return content;
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
                "Ollama non ha restituito embedding validi per il modello richiesto.");
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
                "La richiesta embedding deve contenere almeno un chunk.");
        }

        if (inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new OllamaApiException(
                OllamaErrorKind.InvalidRequest,
                "I chunk vuoti non possono essere inviati a Ollama per gli embedding.");
        }

        OllamaRequestContext context = await BuildContextAsync(cancellationToken);
        object inputPayload = inputs.Count == 1 ? inputs[0] : inputs;
        object requestBody = numCtx.HasValue
            ? new { model = normalizedModelName, input = inputPayload, options = new { num_ctx = numCtx.Value } }
            : (object)new { model = normalizedModelName, input = inputPayload };

        EmbeddingResponse response = await SendAsync<EmbeddingResponse>(
            HttpMethod.Post,
            context,
            "api/embed",
            requestBody,
            cancellationToken);

        if (response.Embeddings.Count != inputs.Count)
        {
            throw new OllamaApiException(
                OllamaErrorKind.UnexpectedResponse,
                "Ollama ha restituito un numero di embedding diverso dal numero di chunk inviati.");
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
                    "Ollama ha restituito una risposta vuota.");
            }

            return payload;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaApiException(
                OllamaErrorKind.Timeout,
                $"Ollama non ha risposto entro {context.Timeout.TotalSeconds:0} secondi.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaApiException(
                OllamaErrorKind.Unreachable,
                $"Non riesco a raggiungere Ollama su {context.BaseUri}. Controlla l'indirizzo e verifica che il servizio sia in esecuzione.",
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

}

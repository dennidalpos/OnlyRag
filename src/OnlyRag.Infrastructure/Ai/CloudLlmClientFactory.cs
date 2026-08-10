using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OnlyRag.Core;
using MsChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace OnlyRag.Infrastructure.Ai;

public interface ICloudLlmClientFactory
{
    IChatClient CreateChatClient(CloudLlmConfiguration config, string? apiKey);
    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(CloudLlmConfiguration config, string? apiKey);
    Task<CloudLlmTestResult> TestConnectionAsync(CloudLlmConfiguration config, string? apiKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates local Ollama clients using native HTTP without the <c>Microsoft.Extensions.AI.Ollama</c> package.
/// </summary>
public static class OllamaLocalClientFactory
{
    public static IChatClient CreateChatClient(HttpClient httpClient, string endpoint, string model)
        => new OllamaHttpChatClient(httpClient, endpoint, model);

    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(HttpClient httpClient, string endpoint, string model)
        => new OllamaHttpEmbeddingGenerator(httpClient, endpoint, model);
}

public sealed class CloudLlmClientFactory : ICloudLlmClientFactory
{
    private readonly HttpClient _httpClient;

    public CloudLlmClientFactory(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public IChatClient CreateChatClient(CloudLlmConfiguration config, string? apiKey)
    {
        return config.Provider switch
        {
            CloudLlmProvider.AzureOpenAi => new OpenAiCompatibleChatClient(_httpClient, CloudLlmEndpointValidator.Validate(config.Provider, GetAzureEndpoint(config)), config.ChatModel, apiKey, isAzure: true, config.ApiVersion),
            CloudLlmProvider.OpenAi => new OpenAiCompatibleChatClient(_httpClient, CloudLlmEndpointValidator.Validate(config.Provider, config.Endpoint), config.ChatModel, apiKey, isAzure: false),
            CloudLlmProvider.Anthropic => new AnthropicChatClient(_httpClient, CloudLlmEndpointValidator.Validate(config.Provider, config.Endpoint), config.ChatModel, apiKey),
            CloudLlmProvider.GoogleGemini => new GoogleGeminiChatClient(_httpClient, CloudLlmEndpointValidator.Validate(config.Provider, config.Endpoint), config.ChatModel, apiKey),
            _ => new OllamaHttpChatClient(_httpClient, string.IsNullOrWhiteSpace(config.Endpoint) ? "http://localhost:11434" : config.Endpoint, config.ChatModel)
        };
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(CloudLlmConfiguration config, string? apiKey)
    {
        return config.Provider switch
        {
            CloudLlmProvider.AzureOpenAi => new OpenAiCompatibleEmbeddingGenerator(_httpClient, CloudLlmEndpointValidator.Validate(config.Provider, GetAzureEndpoint(config)), config.EmbeddingModel, apiKey, isAzure: true, config.ApiVersion),
            CloudLlmProvider.OpenAi => new OpenAiCompatibleEmbeddingGenerator(_httpClient, CloudLlmEndpointValidator.Validate(config.Provider, config.Endpoint), config.EmbeddingModel, apiKey, isAzure: false),
            CloudLlmProvider.GoogleGemini => new GoogleGeminiEmbeddingGenerator(_httpClient, CloudLlmEndpointValidator.Validate(config.Provider, config.Endpoint), config.EmbeddingModel, apiKey),
            _ => new OllamaHttpEmbeddingGenerator(_httpClient, string.IsNullOrWhiteSpace(config.Endpoint) ? "http://localhost:11434" : config.Endpoint, config.EmbeddingModel)
        };
    }

    public async Task<CloudLlmTestResult> TestConnectionAsync(CloudLlmConfiguration config, string? apiKey, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var chatClient = CreateChatClient(config, apiKey);
            var response = await chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Ping test. Respond only with OK.")],
                new ChatOptions { MaxOutputTokens = 10 },
                cancellationToken);

            sw.Stop();
            string responseText = response.Text?.Trim() ?? "";

            return new CloudLlmTestResult(
                Success: true,
                Message: $"Connessione riuscita a {config.Provider} in {sw.ElapsedMilliseconds}ms. Risposta: {responseText}",
                LatencyMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CloudLlmTestResult(
                Success: false,
                Message: $"Errore connettività provider {config.Provider}: {ex.Message}",
                LatencyMs: sw.ElapsedMilliseconds);
        }
    }

    private static string GetAzureEndpoint(CloudLlmConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
        {
            return "https://your-resource.openai.azure.com";
        }
        return config.Endpoint.TrimEnd('/');
    }
}

// -----------------------------------------------------------------------------
// OpenAI & Azure OpenAI Chat Client Wrapper
// -----------------------------------------------------------------------------
internal sealed class OpenAiCompatibleChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly string? _apiKey;
    private readonly bool _isAzure;
    private readonly string _apiVersion;

    public ChatClientMetadata Metadata { get; }

    public OpenAiCompatibleChatClient(HttpClient httpClient, string endpoint, string defaultModel, string? apiKey, bool isAzure = false, string apiVersion = "2024-02-15-preview")
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = defaultModel;
        _apiKey = apiKey;
        _isAzure = isAzure;
        _apiVersion = apiVersion;
        Metadata = new ChatClientMetadata("OpenAICompatible", new Uri(_endpoint), _defaultModel);
    }

    public void Dispose() { }

    public async Task<MsChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        string model = options?.ModelId ?? _defaultModel;
        string url = _isAzure
            ? $"{_endpoint}/openai/deployments/{model}/chat/completions?api-version={_apiVersion}"
            : $"{_endpoint}/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (_isAzure && !string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Add("api-key", _apiKey);
        }
        else if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        var payload = new
        {
            model = _isAzure ? null : model,
            messages = messages.Select(m => new { role = m.Role.Value.ToLowerInvariant(), content = m.Text }).ToList(),
            max_tokens = options?.MaxOutputTokens,
            temperature = options?.Temperature
        };

        request.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        string content = json?["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";

        return new MsChatResponse(new ChatMessage(ChatRole.Assistant, content));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }


    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}

// -----------------------------------------------------------------------------
// OpenAI & Azure OpenAI Embedding Generator Wrapper
// -----------------------------------------------------------------------------
internal sealed class OpenAiCompatibleEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly string? _apiKey;
    private readonly bool _isAzure;
    private readonly string _apiVersion;

    public EmbeddingGeneratorMetadata Metadata { get; }

    public OpenAiCompatibleEmbeddingGenerator(HttpClient httpClient, string endpoint, string defaultModel, string? apiKey, bool isAzure = false, string apiVersion = "2024-02-15-preview")
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = defaultModel;
        _apiKey = apiKey;
        _isAzure = isAzure;
        _apiVersion = apiVersion;
        Metadata = new EmbeddingGeneratorMetadata("OpenAICompatible", new Uri(_endpoint), _defaultModel);
    }

    public void Dispose() { }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        string model = options?.ModelId ?? _defaultModel;
        string url = _isAzure
            ? $"{_endpoint}/openai/deployments/{model}/embeddings?api-version={_apiVersion}"
            : $"{_endpoint}/embeddings";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (_isAzure && !string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Add("api-key", _apiKey);
        }
        else if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        var payload = new
        {
            model = _isAzure ? null : model,
            input = values.ToList()
        };

        request.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var data = json?["data"]?.AsArray();

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        if (data != null)
        {
            foreach (var item in data)
            {
                var vecArray = item?["embedding"]?.AsArray();
                if (vecArray != null)
                {
                    float[] floats = vecArray.Select(v => (float)(v?.GetValue<double>() ?? 0.0)).ToArray();
                    embeddings.Add(new Embedding<float>(floats));
                }
            }
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}

// -----------------------------------------------------------------------------
// Anthropic Claude Chat Client Wrapper
// -----------------------------------------------------------------------------
internal sealed class AnthropicChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly string? _apiKey;

    public ChatClientMetadata Metadata { get; }

    public AnthropicChatClient(HttpClient httpClient, string endpoint, string defaultModel, string? apiKey)
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "claude-3-5-sonnet-20241022" : defaultModel;
        _apiKey = apiKey;
        Metadata = new ChatClientMetadata("Anthropic", new Uri(_endpoint), _defaultModel);
    }

    public void Dispose() { }

    public async Task<MsChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        string model = options?.ModelId ?? _defaultModel;
        string url = $"{_endpoint}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Add("x-api-key", _apiKey);
        }
        request.Headers.Add("anthropic-version", "2023-06-01");

        var payloadMessages = messages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new { role = m.Role == ChatRole.User ? "user" : "assistant", content = m.Text })
            .ToList();

        string? systemPrompt = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text;

        var payload = new
        {
            model = model,
            max_tokens = options?.MaxOutputTokens ?? 1024,
            system = systemPrompt,
            messages = payloadMessages
        };

        request.Content = JsonContent.Create(payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        string content = json?["content"]?[0]?["text"]?.ToString() ?? "";

        return new MsChatResponse(new ChatMessage(ChatRole.Assistant, content));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }


    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}

// -----------------------------------------------------------------------------
// Google Gemini Chat Client Wrapper
// -----------------------------------------------------------------------------
internal sealed class GoogleGeminiChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly string? _apiKey;

    public ChatClientMetadata Metadata { get; }

    public GoogleGeminiChatClient(HttpClient httpClient, string endpoint, string defaultModel, string? apiKey)
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "gemini-1.5-flash" : defaultModel;
        _apiKey = apiKey;
        Metadata = new ChatClientMetadata("GoogleGemini", new Uri(_endpoint), _defaultModel);
    }

    public void Dispose() { }

    public async Task<MsChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        string model = options?.ModelId ?? _defaultModel;
        string url = $"{_endpoint}/models/{model}:generateContent?key={_apiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        var contents = messages.Select(m => new
        {
            role = m.Role == ChatRole.User ? "user" : "model",
            parts = new[] { new { text = m.Text } }
        }).ToList();

        var payload = new { contents = contents };
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        string text = json?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString() ?? "";

        return new MsChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }


    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}

// -----------------------------------------------------------------------------
// Google Gemini Embedding Generator Wrapper
// -----------------------------------------------------------------------------
internal sealed class GoogleGeminiEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly string? _apiKey;

    public EmbeddingGeneratorMetadata Metadata { get; }

    public GoogleGeminiEmbeddingGenerator(HttpClient httpClient, string endpoint, string defaultModel, string? apiKey)
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "text-embedding-004" : defaultModel;
        _apiKey = apiKey;
        Metadata = new EmbeddingGeneratorMetadata("GoogleGemini", new Uri(_endpoint), _defaultModel);
    }

    public void Dispose() { }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        string model = options?.ModelId ?? _defaultModel;
        var result = new GeneratedEmbeddings<Embedding<float>>();

        foreach (string val in values)
        {
            string url = $"{_endpoint}/models/{model}:embedContent?key={_apiKey}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            var payload = new
            {
                model = $"models/{model}",
                content = new { parts = new[] { new { text = val } } }
            };
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var valuesArr = json?["embedding"]?["values"]?.AsArray();
            if (valuesArr != null)
            {
                float[] floats = valuesArr.Select(v => (float)(v?.GetValue<double>() ?? 0.0)).ToArray();
                result.Add(new Embedding<float>(floats));
            }
        }

        return result;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}

// -----------------------------------------------------------------------------
// Local Ollama Chat Client Wrapper
// -----------------------------------------------------------------------------
internal sealed class OllamaHttpChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _defaultModel;

    public ChatClientMetadata Metadata { get; }

    public OllamaHttpChatClient(HttpClient httpClient, string endpoint, string defaultModel)
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "llama3" : defaultModel;
        Metadata = new ChatClientMetadata("OllamaLocal", new Uri(_endpoint), _defaultModel);
    }

    public void Dispose() { }

    public async Task<MsChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        string model = options?.ModelId ?? _defaultModel;
        string url = $"{_endpoint}/api/chat";

        var payloadMessages = messages.Select(m => new { role = m.Role.Value.ToLowerInvariant(), content = m.Text }).ToList();
        var payload = new
        {
            model = model,
            messages = payloadMessages,
            stream = false
        };

        using var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        string content = json?["message"]?["content"]?.ToString() ?? "";

        return new MsChatResponse(new ChatMessage(ChatRole.Assistant, content));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }


    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}

// -----------------------------------------------------------------------------
// Local Ollama Embedding Generator Wrapper
// -----------------------------------------------------------------------------
internal sealed class OllamaHttpEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _defaultModel;

    public EmbeddingGeneratorMetadata Metadata { get; }

    public OllamaHttpEmbeddingGenerator(HttpClient httpClient, string endpoint, string defaultModel)
    {
        _httpClient = httpClient;
        _endpoint = endpoint.TrimEnd('/');
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "nomic-embed-text" : defaultModel;
        Metadata = new EmbeddingGeneratorMetadata("OllamaLocal", new Uri(_endpoint), _defaultModel);
    }

    public void Dispose() { }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        string model = options?.ModelId ?? _defaultModel;
        string url = $"{_endpoint}/api/embed";

        var payload = new
        {
            model = model,
            input = values.ToList()
        };

        using var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        var embeddingsArr = json?["embeddings"]?.AsArray();

        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        if (embeddingsArr != null)
        {
            foreach (var item in embeddingsArr)
            {
                var vecArray = item?.AsArray();
                if (vecArray != null)
                {
                    float[] floats = vecArray.Select(v => (float)(v?.GetValue<double>() ?? 0.0)).ToArray();
                    embeddings.Add(new Embedding<float>(floats));
                }
            }
        }

        return embeddings;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;
using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal sealed class ComfyUiImageGenerationClient : IImageGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;

    public ComfyUiImageGenerationClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public string Provider => ImageGenerationProviderNames.ComfyUi;

    public async Task<ImageGenerationProviderStatus> GetStatusAsync(
        ImageGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        string baseUrl = ImageGenerationSettingsService.NormalizeAndValidateBaseUrl(
            settings.ComfyUiBaseUrl,
            settings.TrustNonLocalEndpoint,
            "ComfyUI");
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(BuildBaseUri(baseUrl), "system_stats"));
            using HttpResponseMessage response = await SendAsync(request, settings.RequestTimeoutSeconds, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Offline(baseUrl, "ComfyUI non ha accettato la richiesta di stato.", "Controlla che ComfyUI sia avviato.");
            }

            return new ImageGenerationProviderStatus(
                Provider,
                "Online",
                true,
                baseUrl,
                "ComfyUI raggiungibile.",
                null);
        }
        catch (ImageGenerationException ex)
        {
            return Offline(baseUrl, ex.Message, "Controlla che ComfyUI sia avviato sulla porta configurata.");
        }
    }

    public async Task<IReadOnlyList<ImageGenerationBinary>> GenerateAsync(
        ImageGenerationSettings settings,
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        string baseUrl = ImageGenerationSettingsService.NormalizeAndValidateBaseUrl(
            settings.ComfyUiBaseUrl,
            settings.TrustNonLocalEndpoint,
            "ComfyUI");
        Uri baseUri = BuildBaseUri(baseUrl);
        ImageGenerationRequest normalized = ImageGenerationRequestValidator.Normalize(request);
        JsonNode workflow = BuildWorkflow(settings, normalized);
        string clientId = Guid.NewGuid().ToString("N");

        using HttpRequestMessage promptRequest = new(HttpMethod.Post, new Uri(baseUri, "prompt"))
        {
            Content = JsonContent.Create(new { prompt = workflow, client_id = clientId }, options: JsonOptions)
        };
        using HttpResponseMessage promptResponse = await SendAsync(promptRequest, settings.RequestTimeoutSeconds, cancellationToken);
        if (!promptResponse.IsSuccessStatusCode)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                await ReadProviderErrorAsync(promptResponse, cancellationToken));
        }

        ComfyPromptResponse? promptPayload =
            await promptResponse.Content.ReadFromJsonAsync<ComfyPromptResponse>(JsonOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(promptPayload?.PromptId))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "ComfyUI non ha restituito un prompt_id.");
        }

        JsonObject history = await WaitForHistoryAsync(
            baseUri,
            promptPayload.PromptId,
            settings.RequestTimeoutSeconds,
            cancellationToken);
        IReadOnlyList<ComfyOutputImage> outputImages = ExtractOutputImages(history, promptPayload.PromptId);
        if (outputImages.Count == 0)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "ComfyUI ha completato il workflow senza immagini in output.");
        }

        List<ImageGenerationBinary> images = [];
        foreach (ComfyOutputImage image in outputImages)
        {
            Uri imageUri = BuildViewUri(baseUri, image);
            using HttpRequestMessage imageRequest = new(HttpMethod.Get, imageUri);
            using HttpResponseMessage imageResponse = await SendAsync(imageRequest, settings.RequestTimeoutSeconds, cancellationToken);
            if (!imageResponse.IsSuccessStatusCode)
            {
                throw new ImageGenerationException(
                    ImageGenerationErrorKind.UnexpectedResponse,
                    await ReadProviderErrorAsync(imageResponse, cancellationToken));
            }

            string mimeType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
            byte[] bytes = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            images.Add(new ImageGenerationBinary(bytes, mimeType, ResolveExtension(mimeType, image.FileName)));
        }

        return images;
    }

    private static ImageGenerationProviderStatus Offline(string baseUrl, string message, string suggestion)
    {
        return new ImageGenerationProviderStatus(
            ImageGenerationProviderNames.ComfyUi,
            "Offline",
            false,
            baseUrl,
            message,
            suggestion);
    }

    private static JsonNode BuildWorkflow(ImageGenerationSettings settings, ImageGenerationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(settings.ComfyUiWorkflowJson))
        {
            JsonNode workflow = JsonNode.Parse(settings.ComfyUiWorkflowJson)
                ?? throw new ImageGenerationException(
                    ImageGenerationErrorKind.InvalidConfiguration,
                    "Il workflow ComfyUI salvato non e valido.");
            ApplyWorkflowPlaceholders(workflow, request);
            return workflow;
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.InvalidRequest,
                "Seleziona un checkpoint ComfyUI o salva un workflow ComfyUI nelle impostazioni immagini.");
        }

        return new JsonObject
        {
            ["1"] = Node("CheckpointLoaderSimple", new JsonObject { ["ckpt_name"] = request.Model }),
            ["2"] = Node("CLIPTextEncode", new JsonObject { ["text"] = request.Prompt, ["clip"] = Link("1", 1) }),
            ["3"] = Node("CLIPTextEncode", new JsonObject { ["text"] = request.NegativePrompt ?? string.Empty, ["clip"] = Link("1", 1) }),
            ["4"] = Node("EmptyLatentImage", new JsonObject
            {
                ["width"] = request.Width,
                ["height"] = request.Height,
                ["batch_size"] = request.BatchSize
            }),
            ["5"] = Node("KSampler", new JsonObject
            {
                ["seed"] = request.Seed ?? Random.Shared.NextInt64(0, long.MaxValue),
                ["steps"] = request.Steps,
                ["cfg"] = 7.0,
                ["sampler_name"] = "euler",
                ["scheduler"] = "normal",
                ["denoise"] = 1.0,
                ["model"] = Link("1", 0),
                ["positive"] = Link("2", 0),
                ["negative"] = Link("3", 0),
                ["latent_image"] = Link("4", 0)
            }),
            ["6"] = Node("VAEDecode", new JsonObject { ["samples"] = Link("5", 0), ["vae"] = Link("1", 2) }),
            ["7"] = Node("SaveImage", new JsonObject { ["filename_prefix"] = "OnlyRag", ["images"] = Link("6", 0) })
        };
    }

    private static JsonArray Link(string nodeId, int outputIndex)
    {
        return new JsonArray(nodeId, outputIndex);
    }

    private static JsonObject Node(string classType, JsonObject inputs)
    {
        return new JsonObject
        {
            ["class_type"] = classType,
            ["inputs"] = inputs
        };
    }

    private static void ApplyWorkflowPlaceholders(JsonNode node, ImageGenerationRequest request)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                if (pair.Value is null)
                {
                    continue;
                }

                JsonNode replacement = ReplacePlaceholder(pair.Value, request);
                if (!ReferenceEquals(pair.Value, replacement))
                {
                    obj[pair.Key] = replacement;
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                if (array[index] is null)
                {
                    continue;
                }

                JsonNode replacement = ReplacePlaceholder(array[index]!, request);
                if (!ReferenceEquals(array[index], replacement))
                {
                    array[index] = replacement;
                }
            }
        }
    }

    private static JsonNode ReplacePlaceholder(JsonNode node, ImageGenerationRequest request)
    {
        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            return text switch
            {
                "{{prompt}}" => JsonValue.Create(request.Prompt)!,
                "{{negative_prompt}}" => JsonValue.Create(request.NegativePrompt ?? string.Empty)!,
                "{{model}}" => JsonValue.Create(request.Model ?? string.Empty)!,
                "{{width}}" => JsonValue.Create(request.Width)!,
                "{{height}}" => JsonValue.Create(request.Height)!,
                "{{steps}}" => JsonValue.Create(request.Steps)!,
                "{{batch_size}}" => JsonValue.Create(request.BatchSize)!,
                "{{seed}}" => JsonValue.Create(request.Seed ?? Random.Shared.NextInt64(0, long.MaxValue))!,
                _ => node
            };
        }

        ApplyWorkflowPlaceholders(node, request);
        return node;
    }

    private async Task<JsonObject> WaitForHistoryAsync(
        Uri baseUri,
        string promptId,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(ImageGenerationSettingsService.ValidateRequestTimeoutSeconds(timeoutSeconds));
        while (DateTimeOffset.UtcNow < deadline)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, $"history/{Uri.EscapeDataString(promptId)}"));
            using HttpResponseMessage response = await SendAsync(request, timeoutSeconds, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                JsonObject? payload = await response.Content.ReadFromJsonAsync<JsonObject>(JsonOptions, cancellationToken);
                if (payload is not null && payload.Count > 0)
                {
                    return payload;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        throw new ImageGenerationException(
            ImageGenerationErrorKind.Timeout,
            "ComfyUI non ha completato il workflow entro il timeout configurato.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(ImageGenerationSettingsService.ValidateRequestTimeoutSeconds(timeoutSeconds)));
        try
        {
            return await httpClient.SendAsync(request, timeoutSource.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Timeout,
                "ComfyUI non ha risposto entro il timeout configurato.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Unreachable,
                "ComfyUI non e raggiungibile.",
                ex);
        }
    }

    private static IReadOnlyList<ComfyOutputImage> ExtractOutputImages(JsonObject history, string promptId)
    {
        JsonNode? root = history[promptId] ?? history.FirstOrDefault().Value;
        JsonObject? outputs = root?["outputs"] as JsonObject;
        if (outputs is null)
        {
            return [];
        }

        List<ComfyOutputImage> images = [];
        foreach (KeyValuePair<string, JsonNode?> output in outputs)
        {
            if (output.Value?["images"] is not JsonArray imageArray)
            {
                continue;
            }

            foreach (JsonNode? item in imageArray)
            {
                string? fileName = item?["filename"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                images.Add(new ComfyOutputImage(
                    fileName,
                    item?["subfolder"]?.GetValue<string>() ?? string.Empty,
                    item?["type"]?.GetValue<string>() ?? "output"));
            }
        }

        return images;
    }

    private static Uri BuildViewUri(Uri baseUri, ComfyOutputImage image)
    {
        string query = $"filename={HttpUtility.UrlEncode(image.FileName)}&subfolder={HttpUtility.UrlEncode(image.Subfolder)}&type={HttpUtility.UrlEncode(image.Type)}";
        return new Uri(baseUri, $"view?{query}");
    }

    private static string ResolveExtension(string mimeType, string fileName)
    {
        string extension = Path.GetExtension(fileName);
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            return extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : extension.ToLowerInvariant();
        }

        return mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
    }

    private static async Task<string> ReadProviderErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body)
            ? $"ComfyUI ha risposto con HTTP {(int)response.StatusCode}."
            : $"ComfyUI ha risposto con errore: {body}";
    }

    private static Uri BuildBaseUri(string baseUrl)
    {
        return new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute);
    }

    private sealed record ComfyPromptResponse(
        [property: JsonPropertyName("prompt_id")] string PromptId);

    private sealed record ComfyOutputImage(string FileName, string Subfolder, string Type);
}

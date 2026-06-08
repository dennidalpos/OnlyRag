using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal sealed class Automatic1111ImageGenerationClient : IImageGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;

    public Automatic1111ImageGenerationClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public string Provider => ImageGenerationProviderNames.Automatic1111;

    public async Task<ImageGenerationProviderStatus> GetStatusAsync(
        ImageGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        string baseUrl = ImageGenerationSettingsService.NormalizeAndValidateBaseUrl(
            settings.Automatic1111BaseUrl,
            settings.TrustNonLocalEndpoint,
            "Automatic1111");
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(BuildBaseUri(baseUrl), "sdapi/v1/sd-models"));
            using HttpResponseMessage response = await SendAsync(request, settings.RequestTimeoutSeconds, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Offline(baseUrl, "Automatic1111 non ha accettato la richiesta API.", "Avvia WebUI con il flag --api.");
            }

            return new ImageGenerationProviderStatus(
                Provider,
                "Online",
                true,
                baseUrl,
                "Automatic1111 raggiungibile.",
                null);
        }
        catch (ImageGenerationException ex)
        {
            return Offline(baseUrl, ex.Message, "Controlla che Automatic1111 sia avviato e che --api sia attivo.");
        }
    }

    public async Task<IReadOnlyList<ImageGenerationBinary>> GenerateAsync(
        ImageGenerationSettings settings,
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        string baseUrl = ImageGenerationSettingsService.NormalizeAndValidateBaseUrl(
            settings.Automatic1111BaseUrl,
            settings.TrustNonLocalEndpoint,
            "Automatic1111");
        ImageGenerationRequest normalized = ImageGenerationRequestValidator.Normalize(request);
        string? model = string.IsNullOrWhiteSpace(normalized.Model)
            ? settings.Automatic1111Model
            : normalized.Model.Trim();

        object body = string.IsNullOrWhiteSpace(model)
            ? new
            {
                prompt = normalized.Prompt,
                negative_prompt = normalized.NegativePrompt ?? string.Empty,
                width = normalized.Width,
                height = normalized.Height,
                steps = normalized.Steps,
                batch_size = normalized.BatchSize,
                seed = normalized.Seed ?? -1
            }
            : new
            {
                prompt = normalized.Prompt,
                negative_prompt = normalized.NegativePrompt ?? string.Empty,
                width = normalized.Width,
                height = normalized.Height,
                steps = normalized.Steps,
                batch_size = normalized.BatchSize,
                seed = normalized.Seed ?? -1,
                override_settings = new { sd_model_checkpoint = model }
            };

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(BuildBaseUri(baseUrl), "sdapi/v1/txt2img"))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using HttpResponseMessage response = await SendAsync(httpRequest, settings.RequestTimeoutSeconds, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                await ReadProviderErrorAsync(response, cancellationToken));
        }

        Automatic1111Txt2ImgResponse? payload =
            await response.Content.ReadFromJsonAsync<Automatic1111Txt2ImgResponse>(JsonOptions, cancellationToken);
        if (payload?.Images is null || payload.Images.Count == 0)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Automatic1111 non ha restituito immagini.");
        }

        return payload.Images.Select(DecodeBase64Image).ToArray();
    }

    private static ImageGenerationProviderStatus Offline(string baseUrl, string message, string suggestion)
    {
        return new ImageGenerationProviderStatus(
            ImageGenerationProviderNames.Automatic1111,
            "Offline",
            false,
            baseUrl,
            message,
            suggestion);
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
                "Automatic1111 non ha risposto entro il timeout configurato.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.Unreachable,
                "Automatic1111 non e raggiungibile.",
                ex);
        }
    }

    private static async Task<string> ReadProviderErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body)
            ? $"Automatic1111 ha risposto con HTTP {(int)response.StatusCode}."
            : $"Automatic1111 ha risposto con errore: {body}";
    }

    private static ImageGenerationBinary DecodeBase64Image(string raw)
    {
        string value = raw;
        string mimeType = "image/png";
        int commaIndex = raw.IndexOf(',');
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex > 5)
        {
            string metadata = raw[5..commaIndex];
            int semicolonIndex = metadata.IndexOf(';');
            mimeType = semicolonIndex > 0 ? metadata[..semicolonIndex] : metadata;
            value = raw[(commaIndex + 1)..];
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(value);
            return new ImageGenerationBinary(bytes, mimeType, ResolveExtension(mimeType));
        }
        catch (FormatException ex)
        {
            throw new ImageGenerationException(
                ImageGenerationErrorKind.UnexpectedResponse,
                "Automatic1111 ha restituito un'immagine base64 non valida.",
                ex);
        }
    }

    private static string ResolveExtension(string mimeType)
    {
        return mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
    }

    private static Uri BuildBaseUri(string baseUrl)
    {
        return new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute);
    }

    private sealed record Automatic1111Txt2ImgResponse(
        [property: JsonPropertyName("images")] IReadOnlyList<string> Images);
}


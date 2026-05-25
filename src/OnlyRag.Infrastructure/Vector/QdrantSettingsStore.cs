using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Vector;

public sealed class QdrantSettingsStore
{
    private const string EndpointKey = "qdrant.grpcEndpoint";
    private const string ApiKeyKey = "qdrant.apiKey";
    private const string TrustRemoteKey = "qdrant.trustNonLoopbackEndpoint";
    private const string RequireTlsKey = "qdrant.requireTlsForRemoteEndpoint";
    private const string UseLocalKey = "qdrant.useLocalBundledServer";
    private const string LocalPortKey = "qdrant.localGrpcPort";
    private const string TimeoutKey = "qdrant.requestTimeoutSeconds";

    private readonly ISettingsRepository settings;

    public QdrantSettingsStore(ISettingsRepository settings)
    {
        this.settings = settings;
    }

    public async Task<QdrantSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        QdrantSettings defaults = new();
        return Normalize(new QdrantSettings(
            GrpcEndpoint: await settings.GetValueAsync(EndpointKey, cancellationToken) ?? defaults.GrpcEndpoint,
            ApiKey: NormalizeOptional(await settings.GetValueAsync(ApiKeyKey, cancellationToken)),
            TrustNonLoopbackEndpoint: ParseBool(await settings.GetValueAsync(TrustRemoteKey, cancellationToken), defaults.TrustNonLoopbackEndpoint),
            RequireTlsForRemoteEndpoint: ParseBool(await settings.GetValueAsync(RequireTlsKey, cancellationToken), defaults.RequireTlsForRemoteEndpoint),
            UseLocalBundledServer: ParseBool(await settings.GetValueAsync(UseLocalKey, cancellationToken), defaults.UseLocalBundledServer),
            LocalGrpcPort: ParseInt(await settings.GetValueAsync(LocalPortKey, cancellationToken), defaults.LocalGrpcPort, 1, 65535),
            RequestTimeoutSeconds: ParseInt(await settings.GetValueAsync(TimeoutKey, cancellationToken), defaults.RequestTimeoutSeconds, 1, 300)));
    }

    public async Task<QdrantSettings> UpdateAsync(QdrantSettings request, CancellationToken cancellationToken = default)
    {
        QdrantSettings normalized = Normalize(request);
        await settings.UpsertAsync(EndpointKey, normalized.GrpcEndpoint, cancellationToken);
        await settings.UpsertAsync(ApiKeyKey, normalized.ApiKey ?? string.Empty, cancellationToken);
        await settings.UpsertAsync(TrustRemoteKey, normalized.TrustNonLoopbackEndpoint.ToString(), cancellationToken);
        await settings.UpsertAsync(RequireTlsKey, normalized.RequireTlsForRemoteEndpoint.ToString(), cancellationToken);
        await settings.UpsertAsync(UseLocalKey, normalized.UseLocalBundledServer.ToString(), cancellationToken);
        await settings.UpsertAsync(LocalPortKey, normalized.LocalGrpcPort.ToString(), cancellationToken);
        await settings.UpsertAsync(TimeoutKey, normalized.RequestTimeoutSeconds.ToString(), cancellationToken);
        return normalized;
    }

    public static QdrantSettings Normalize(QdrantSettings request)
    {
        Uri endpoint = ParseEndpoint(request.GrpcEndpoint);
        bool loopback = IsLoopback(endpoint);
        if (!loopback && !request.TrustNonLoopbackEndpoint)
        {
            throw new InvalidOperationException(
                "Endpoint Qdrant non loopback non considerato attendibile. Abilita esplicitamente il trust per endpoint remoti.");
        }

        if (!loopback && request.RequireTlsForRemoteEndpoint && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Endpoint Qdrant remoto senza TLS bloccato. Usa https oppure disabilita esplicitamente la policy TLS per endpoint attendibili.");
        }

        return request with
        {
            GrpcEndpoint = endpoint.ToString().TrimEnd('/'),
            ApiKey = NormalizeOptional(request.ApiKey),
            LocalGrpcPort = Math.Clamp(request.LocalGrpcPort, 1, 65535),
            RequestTimeoutSeconds = Math.Clamp(request.RequestTimeoutSeconds, 1, 300)
        };
    }

    public static Uri ParseEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || uri.Port <= 0)
        {
            throw new InvalidOperationException("Endpoint Qdrant gRPC non valido. Usa http://127.0.0.1:6334 o https://host:port senza credenziali nell'URL.");
        }

        return uri;
    }

    public static bool IsLoopback(Uri uri)
    {
        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (System.Net.IPAddress.TryParse(uri.Host, out System.Net.IPAddress? address)
                && System.Net.IPAddress.IsLoopback(address));
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out bool parsed) ? parsed : fallback;
    }

    private static int ParseInt(string? value, int fallback, int min, int max)
    {
        return int.TryParse(value, out int parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }
}

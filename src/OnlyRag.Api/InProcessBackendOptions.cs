using System.Net;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Api.Images;

namespace OnlyRag.Api;

public sealed record InProcessBackendOptions
{
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    public int Port { get; init; }

    public string? SessionToken { get; init; }

    public LocalDocumentLibraryLimits DocumentLibraryLimits { get; init; } = LocalDocumentLibraryLimits.Default;

    public ILocalProcessLauncher? ProcessLauncher { get; init; }

    public IQdrantVectorStore? QdrantVectorStore { get; init; }

    public IImageGenerationEngine? ImageGenerationEngine { get; init; }

    public bool EnableDevelopmentCorsOrigins { get; init; } =
#if DEBUG
        true;
#else
        false;
#endif
}

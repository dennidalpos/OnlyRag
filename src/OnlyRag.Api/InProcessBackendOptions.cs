using System.Net;

namespace OnlyRag.Api;

public sealed record InProcessBackendOptions
{
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    public int Port { get; init; }
}

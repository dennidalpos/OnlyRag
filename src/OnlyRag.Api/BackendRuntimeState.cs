namespace OnlyRag.Api;

internal sealed class BackendRuntimeState
{
    public BackendRuntimeState(DateTimeOffset startedAtUtc)
    {
        StartedAtUtc = startedAtUtc;
    }

    public DateTimeOffset StartedAtUtc { get; }

    public Uri? BaseUri { get; set; }

    public string DatabaseStatus { get; set; } = "NotInitialized";
}

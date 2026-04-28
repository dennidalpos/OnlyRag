namespace OnlyRag.Core;

public sealed record AppStatusResponse(
    string Backend,
    string Database,
    string JobQueue,
    string Ollama,
    DateTimeOffset StartedAtUtc,
    bool LowResourceMode);

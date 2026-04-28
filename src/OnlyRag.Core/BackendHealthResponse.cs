namespace OnlyRag.Core;

public sealed record BackendHealthResponse(string Status, DateTimeOffset StartedAtUtc);

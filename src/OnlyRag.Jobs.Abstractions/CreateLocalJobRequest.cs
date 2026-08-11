namespace OnlyRag.Jobs.Abstractions;

public sealed record CreateLocalJobRequest(
    string Type,
    string PayloadJson,
    int Priority = 0,
    int? MaxRetries = null);

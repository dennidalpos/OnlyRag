namespace OnlyRag.Core;

public sealed record DocumentSearchRequest(
    string Query,
    IReadOnlyList<long> DocumentIds,
    int? TopK);

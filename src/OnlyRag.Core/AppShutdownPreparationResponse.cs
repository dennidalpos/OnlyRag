namespace OnlyRag.Core;

public sealed record AppShutdownPreparationResponse(
    int ActiveJobCount,
    int CancelledJobCount,
    string[] UnstoppedJobIds)
{
    public bool IsComplete => UnstoppedJobIds.Length == 0;
}

using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Api;

internal static class JobStatusExtensions
{
    public static bool IsActive(this JobStatus status)
    {
        return status is JobStatus.Pending or JobStatus.Running or JobStatus.Pausing or JobStatus.Paused;
    }

    public static bool IsRunningOrPausing(this JobStatus status)
    {
        return status is JobStatus.Running or JobStatus.Pausing;
    }
}

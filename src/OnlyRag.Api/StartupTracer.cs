using System.Collections.Concurrent;
using System.Diagnostics;

namespace OnlyRag.Api;

/// <summary>
/// Records startup milestones with high-precision elapsed times.
/// Exposed via GET /api/diagnostics/startup-trace for live diagnostics.
/// </summary>
public sealed class StartupTracer
{
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly ConcurrentQueue<StartupMilestone> _milestones = new();

    public void Record(string phase)
    {
        _milestones.Enqueue(new StartupMilestone(phase, _wall.Elapsed));
    }

    public IReadOnlyList<StartupMilestone> GetTrace() => _milestones.ToArray();

    public TimeSpan Elapsed => _wall.Elapsed;
}

public sealed record StartupMilestone(string Phase, TimeSpan ElapsedSinceStart)
{
    public string FormattedElapsed => $"{ElapsedSinceStart.TotalMilliseconds:F0}ms";
}

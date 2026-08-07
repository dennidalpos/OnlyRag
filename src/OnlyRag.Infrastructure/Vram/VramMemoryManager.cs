using System.Collections.Concurrent;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Vram;

public sealed class VramMemoryManager : IVramMemoryManager
{
    private readonly ConcurrentDictionary<string, Action> _registeredSessions = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterSession(string sessionName, Action releaseCallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentNullException.ThrowIfNull(releaseCallback);
        _registeredSessions[sessionName] = releaseCallback;
    }

    public void UnregisterSession(string sessionName)
    {
        if (!string.IsNullOrWhiteSpace(sessionName))
        {
            _registeredSessions.TryRemove(sessionName, out _);
        }
    }

    public void EvictIdleSessions()
    {
        ForceVramCleanup();
    }

    public void ForceVramCleanup()
    {
        foreach (var kvp in _registeredSessions)
        {
            try
            {
                kvp.Value();
            }
            catch
            {
                // Soft ignore individual cleanup errors
            }
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public IReadOnlyDictionary<string, bool> GetActiveSessions()
    {
        return _registeredSessions.ToDictionary(k => k.Key, _ => true);
    }
}

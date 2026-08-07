namespace OnlyRag.Core;

public interface IVramMemoryManager
{
    void RegisterSession(string sessionName, Action releaseCallback);
    void UnregisterSession(string sessionName);
    void EvictIdleSessions();
    void ForceVramCleanup();
    IReadOnlyDictionary<string, bool> GetActiveSessions();
}

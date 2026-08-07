using OnlyRag.Infrastructure.Vram;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class VramMemoryManagerTests
{
    [Fact]
    public void VramMemoryManager_RegisterAndForceCleanup_InvokesCallback()
    {
        var manager = new VramMemoryManager();
        bool released = false;

        manager.RegisterSession("test-session", () => released = true);
        var active = manager.GetActiveSessions();

        Assert.True(active.ContainsKey("test-session"));

        manager.ForceVramCleanup();

        Assert.True(released);
    }

    [Fact]
    public void VramMemoryManager_UnregisterSession_RemovesFromActive()
    {
        var manager = new VramMemoryManager();

        manager.RegisterSession("session-1", () => { });
        manager.UnregisterSession("session-1");

        var active = manager.GetActiveSessions();
        Assert.False(active.ContainsKey("session-1"));
    }
}

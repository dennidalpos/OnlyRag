using OnlyRag.Api;
using OnlyRag.Core;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class QdrantAutoHealingTests
{
    [Fact]
    public void QdrantProcessSupervisor_RecordAutoHeal_IncrementsCounterAndSetsTimestamp()
    {
        await using var supervisor = new QdrantProcessSupervisor();

        Assert.Equal(0, supervisor.AutoHealRestartCount);
        Assert.Null(supervisor.LastAutoHealedAtUtc);

        supervisor.RecordAutoHeal();

        Assert.Equal(1, supervisor.AutoHealRestartCount);
        Assert.NotNull(supervisor.LastAutoHealedAtUtc);
        Assert.True(supervisor.LastAutoHealedAtUtc <= DateTimeOffset.UtcNow);

        supervisor.RecordAutoHeal();
        Assert.Equal(2, supervisor.AutoHealRestartCount);
    }
}

using OnlyRag.Api;
using Xunit;

namespace OnlyRag.Api.Tests;

public class AgentCycleGuardTests
{
    [Fact]
    public void IsCyclicPatternDetected_ReturnsFalse_WhenHistoryCountLessThan3()
    {
        var history = new List<string> { "read_file:file1.cs", "read_file:file2.cs" };
        Assert.False(AgentCycleGuard.IsCyclicPatternDetected(history));
    }

    [Fact]
    public void IsCyclicPatternDetected_ReturnsTrue_When3ExactConsecutiveCallsWithIdenticalArguments()
    {
        var history = new List<string>
        {
            "reflect_step:{\"stepId\":\"1\",\"status\":\"completed\",\"learnings\":\"same learning\"}",
            "reflect_step:{\"stepId\":\"1\",\"status\":\"completed\",\"learnings\":\"same learning\"}",
            "reflect_step:{\"stepId\":\"1\",\"status\":\"completed\",\"learnings\":\"same learning\"}"
        };

        Assert.True(AgentCycleGuard.IsCyclicPatternDetected(history));
    }

    [Fact]
    public void IsCyclicPatternDetected_ReturnsFalse_WhenReflectStepsHaveDifferentLearnings()
    {
        var history = new List<string>
        {
            "reflect_step:{\"stepId\":\"1\",\"status\":\"completed\",\"learnings\":\"Learned fact A\"}",
            "reflect_step:{\"stepId\":\"2\",\"status\":\"completed\",\"learnings\":\"Learned fact B\"}",
            "reflect_step:{\"stepId\":\"3\",\"status\":\"completed\",\"learnings\":\"Learned fact C\"}"
        };

        Assert.False(AgentCycleGuard.IsCyclicPatternDetected(history));
    }

    [Fact]
    public void IsCyclicPatternDetected_ReturnsFalse_WhenToolNamesRepeatOnDifferentFiles()
    {
        var history = new List<string>
        {
            "read_file:src/File1.cs",
            "replace_file_content:src/File1.cs",
            "read_file:src/File2.cs",
            "replace_file_content:src/File2.cs",
            "read_file:src/File3.cs",
            "replace_file_content:src/File3.cs"
        };

        Assert.False(AgentCycleGuard.IsCyclicPatternDetected(history));
    }

    [Fact]
    public void IsCyclicPatternDetected_ReturnsTrue_WhenExactPairPatternRepeats3Times()
    {
        var history = new List<string>
        {
            "read_file:src/File1.cs",
            "replace_file_content:src/File1.cs",
            "read_file:src/File1.cs",
            "replace_file_content:src/File1.cs",
            "read_file:src/File1.cs",
            "replace_file_content:src/File1.cs"
        };

        Assert.True(AgentCycleGuard.IsCyclicPatternDetected(history));
    }
}

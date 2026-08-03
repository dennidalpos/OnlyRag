using OnlyRag.Infrastructure.Vector;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public class QdrantHnswTunerTests
{
    [Theory]
    [InlineData(500, 16, 100)]
    [InlineData(50_000, 32, 160)]
    [InlineData(500_000, 64, 256)]
    public void ComputeOptimalParameters_AdaptsToCollectionSize(ulong count, ulong expectedM, ulong expectedEfConstruct)
    {
        var parameters = QdrantHnswTuner.ComputeOptimalParameters(count);

        Assert.Equal(expectedM, parameters.M);
        Assert.Equal(expectedEfConstruct, parameters.EfConstruct);
    }
}

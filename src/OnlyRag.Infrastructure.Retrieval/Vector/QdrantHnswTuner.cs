using Qdrant.Client.Grpc;

namespace OnlyRag.Infrastructure.Vector;

public static class QdrantHnswTuner
{
    public record HnswParameters(ulong M, ulong EfConstruct);

    public static HnswParameters ComputeOptimalParameters(ulong estimatedVectorCount)
    {
        return estimatedVectorCount switch
        {
            < 10_000 => new HnswParameters(M: 16, EfConstruct: 100),
            < 100_000 => new HnswParameters(M: 32, EfConstruct: 160),
            _ => new HnswParameters(M: 64, EfConstruct: 256)
        };
    }

    public static HnswConfigDiff BuildHnswConfigDiff(ulong currentVectorCount)
    {
        HnswParameters paramsObj = ComputeOptimalParameters(currentVectorCount);
        return new HnswConfigDiff
        {
            M = paramsObj.M,
            EfConstruct = paramsObj.EfConstruct
        };
    }
}

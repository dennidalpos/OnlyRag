namespace OnlyRag.Core;

public enum QueryTransformationStrategy
{
    None = 0,
    MultiQuery = 1,
    SubQuery = 2,
    HyDE = 3
}

public sealed record RetrievalSettings(
    bool EnableReRanker = true,
    double ReRankerCutoffThreshold = 0.35,
    int TopCandidatesCount = 40,
    int FinalTopK = 5,
    QueryTransformationStrategy TransformationStrategy = QueryTransformationStrategy.MultiQuery,
    int ChildChunkTokens = 150,
    int ParentChunkTokens = 1000,
    double CragConfidenceThreshold = 0.30)
{
    public static RetrievalSettings Default { get; } = new();
}

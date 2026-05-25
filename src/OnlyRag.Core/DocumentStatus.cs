namespace OnlyRag.Core;

public enum DocumentStatus
{
    Imported,
    Queued,
    Processing,
    Indexed,
    RequiresEmbeddingRebuild,
    RequiresAdditionalComponent,
    Failed
}

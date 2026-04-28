namespace OnlyRag.Infrastructure.Storage;

public sealed record SqliteEmbeddingVectorStoreDescriptor : IEmbeddingVectorStoreDescriptor
{
    public string ProviderName => "SQLite BLOB embeddings";

    public bool RequiresExtension => false;

    public bool VectorExtensionReserved => true;
}

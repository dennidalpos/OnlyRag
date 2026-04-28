namespace OnlyRag.Infrastructure.Storage;

public interface IEmbeddingVectorStoreDescriptor
{
    string ProviderName { get; }

    bool RequiresExtension { get; }
}

namespace OnlyRag.Infrastructure.Retrieval;

public interface ILlmQueryExpander
{
    Task<string?> GenerateExpansionAsync(string prompt, CancellationToken cancellationToken = default);
}

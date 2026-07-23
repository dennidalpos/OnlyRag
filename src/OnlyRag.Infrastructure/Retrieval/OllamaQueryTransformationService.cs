using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class OllamaQueryTransformationService : IQueryTransformationService
{
    private static readonly string[] SubQuerySeparators = [" e ", " E "];

    public Task<QueryTransformationResult> TransformAsync(
        string query,
        QueryTransformationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        string trimmed = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || strategy == QueryTransformationStrategy.None)
        {
            return Task.FromResult(new QueryTransformationResult(trimmed, [trimmed], QueryTransformationStrategy.None));
        }

        List<string> variants = strategy switch
        {
            QueryTransformationStrategy.MultiQuery => GenerateMultiQueryVariants(trimmed),
            QueryTransformationStrategy.SubQuery => GenerateSubQueryVariants(trimmed),
            QueryTransformationStrategy.HyDE => GenerateHyDeVariants(trimmed),
            _ => [trimmed]
        };

        return Task.FromResult(new QueryTransformationResult(trimmed, variants, strategy));
    }

    private static List<string> GenerateMultiQueryVariants(string query)
    {
        List<string> list = [query];
        list.Add($"{query} dettagli spiegazione");
        list.Add($"{query} definizione e contesto");
        return list.Distinct().ToList();
    }

    private static List<string> GenerateSubQueryVariants(string query)
    {
        List<string> list = [query];
        if (query.Contains(" e ", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = query.Split(SubQuerySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string part in parts)
            {
                if (part.Length > 3)
                {
                    list.Add(part);
                }
            }
        }

        return list.Distinct().ToList();
    }

    private static List<string> GenerateHyDeVariants(string query)
    {
        List<string> list = [query];
        list.Add($"Documento riguardante: {query}");
        return list.Distinct().ToList();
    }
}

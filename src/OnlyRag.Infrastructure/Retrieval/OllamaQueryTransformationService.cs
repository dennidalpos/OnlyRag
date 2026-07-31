using System.Collections.Concurrent;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class OllamaQueryTransformationService : IQueryTransformationService
{
    private const int MaxCacheEntries = 500;
    private readonly ILlmQueryExpander? llmExpander;
    private readonly ILoggingService? logger;
    private readonly ConcurrentDictionary<(string Query, QueryTransformationStrategy Strategy), List<string>> llmCache = new();

    public OllamaQueryTransformationService(
        ILlmQueryExpander? llmExpander = null,
        ILoggingService? logger = null)
    {
        this.llmExpander = llmExpander;
        this.logger = logger;
    }

    public async Task<QueryTransformationResult> TransformAsync(
        string query,
        QueryTransformationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        string trimmed = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || strategy == QueryTransformationStrategy.None)
        {
            return new QueryTransformationResult(trimmed, [trimmed], QueryTransformationStrategy.None);
        }

        // For HyDE and SubQuery, use LLM when available; otherwise fall back to MultiQuery heuristics.
        QueryTransformationStrategy effectiveStrategy = strategy;
        if (llmExpander is null && strategy is QueryTransformationStrategy.HyDE or QueryTransformationStrategy.SubQuery)
        {
            effectiveStrategy = QueryTransformationStrategy.MultiQuery;
            logger?.LogInfo("OllamaQueryTransformationService",
                $"Strategia '{strategy}' richiede LLM non disponibile, fallback a MultiQuery euristica.");
        }

        List<string> variants = effectiveStrategy switch
        {
            QueryTransformationStrategy.MultiQuery => GenerateMultiQueryVariants(trimmed),
            // SubQuery and HyDE with no LLM already fell back to MultiQuery above.
            // SubQuery/HyDE with LLM start with the original query; LLM variants are appended below.
            QueryTransformationStrategy.SubQuery => [trimmed],
            QueryTransformationStrategy.HyDE => [trimmed],
            _ => [trimmed]
        };

        if (llmExpander is not null)
        {
            try
            {
                List<string> llmVariants = await GenerateLlmVariantsAsync(trimmed, effectiveStrategy, cancellationToken);
                if (llmVariants.Count > 0)
                {
                    variants.AddRange(llmVariants);
                    logger?.LogInfo("OllamaQueryTransformationService",
                        $"[LLM EXPANSION SUCCESS] Generate {llmVariants.Count} varianti tramite LLM per la strategia '{effectiveStrategy}'.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("OllamaQueryTransformationService",
                    $"Errore nell'espansione LLM, utilizzate solo euristiche: {ex.Message}");
            }
        }

        List<string> distinctVariants = variants.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new QueryTransformationResult(trimmed, distinctVariants, effectiveStrategy);
    }

    private async Task<List<string>> GenerateLlmVariantsAsync(
        string query,
        QueryTransformationStrategy strategy,
        CancellationToken cancellationToken)
    {
        var cacheKey = (query.ToLowerInvariant(), strategy);
        if (llmCache.TryGetValue(cacheKey, out var cached))
        {
            logger?.LogInfo("OllamaQueryTransformationService",
                $"[LLM EXPANSION CACHE HIT] Trovate {cached.Count} varianti in cache per query '{query}' ({strategy}).");
            return cached;
        }

        string prompt = strategy switch
        {
            QueryTransformationStrategy.MultiQuery =>
                $"Genera 3 riformulazioni alternative della seguente query di ricerca, una per riga, conservando il significato originale:\n\"{query}\"",
            QueryTransformationStrategy.SubQuery =>
                $"Scomponi la seguente query di ricerca in 2-3 sotto-query più semplici e focalizzate, una per riga. Mantieni intatti nomi propri ed entità composte:\n\"{query}\"",
            QueryTransformationStrategy.HyDE =>
                $"Scrivi un breve estratto (2-3 frasi) di un ipotetico documento che risponda alla domanda o argomento seguente:\n\"{query}\"",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(prompt) || llmExpander is null)
        {
            return [];
        }

        string? rawResponse = await llmExpander.GenerateExpansionAsync(prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return [];
        }

        List<string> list = [];
        string[] lines = rawResponse.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            string cleanLine = CleanLine(line);
            if (cleanLine.Length > 2 && !string.Equals(cleanLine, query, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(cleanLine);
            }
        }

        if (list.Count > 0)
        {
            EvictCacheIfNeeded();
            llmCache[cacheKey] = list;
        }

        return list;
    }

    private void EvictCacheIfNeeded()
    {
        if (llmCache.Count < MaxCacheEntries)
        {
            return;
        }

        // Remove roughly half the cache entries to avoid frequent evictions.
        int toRemove = llmCache.Count / 2;
        int removed = 0;
        foreach (var key in llmCache.Keys)
        {
            if (removed >= toRemove)
            {
                break;
            }

            if (llmCache.TryRemove(key, out _))
            {
                removed++;
            }
        }
    }

    private static string CleanLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
        {
            return trimmed[2..].Trim(' ', '"', '\'');
        }
        int dotIndex = trimmed.IndexOf('.');
        if (dotIndex > 0 && dotIndex <= 3 && char.IsDigit(trimmed[0]))
        {
            return trimmed[(dotIndex + 1)..].Trim(' ', '"', '\'');
        }
        return trimmed.Trim(' ', '"', '\'');
    }

    private static List<string> GenerateMultiQueryVariants(string query)
    {
        List<string> list = [query];

        string[] stopWords =
        [
            "il", "lo", "la", "le", "gli", "un", "una", "uno", "di", "del", "della", "dei", "degli",
            "delle", "da", "dal", "dalla", "a", "ad", "al", "alla", "in", "nel", "nella", "nei",
            "nelle", "su", "sul", "sulla", "sui", "sulle", "con", "per", "tra", "fra",
            "the", "an", "of", "to", "on", "at", "by", "for", "with", "is", "are", "was"
        ];
        var keywords = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w.ToLowerInvariant()))
            .ToList();
        if (keywords.Count > 0 && keywords.Count < query.Split(' ').Length)
        {
            list.Add(string.Join(" ", keywords));
        }

        foreach (string prefix in new[]
        {
            "cosa ", "come ", "che cosa ", "what is ", "what are ", "how to ", "how ", "perché ", "quando "
        })
        {
            if (query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string noun = query[prefix.Length..].Trim();
                if (noun.Length > 2) list.Add(noun);
                break;
            }
        }

        string expanded = query
            .Replace("API", "application programming interface", StringComparison.OrdinalIgnoreCase)
            .Replace("DB", "database", StringComparison.OrdinalIgnoreCase)
            .Replace("ML", "machine learning", StringComparison.OrdinalIgnoreCase)
            .Replace("LLM", "large language model", StringComparison.OrdinalIgnoreCase)
            .Replace("RAG", "retrieval augmented generation", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(expanded, query, StringComparison.OrdinalIgnoreCase))
        {
            list.Add(expanded);
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

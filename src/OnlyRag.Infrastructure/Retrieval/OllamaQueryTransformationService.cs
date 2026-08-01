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
    private readonly LinkedList<(string Query, QueryTransformationStrategy Strategy)> cacheOrder = new();
    private readonly object cacheLock = new();

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

        QueryTransformationStrategy effectiveStrategy = strategy;
        List<string> variants = strategy switch
        {
            QueryTransformationStrategy.MultiQuery => GenerateMultiQueryVariants(trimmed),
            QueryTransformationStrategy.SubQuery => [trimmed],
            QueryTransformationStrategy.HyDE => GenerateZeroShotHydeVariants(trimmed),
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
                        $"[LLM EXPANSION SUCCESS] Generated {llmVariants.Count} variants via LLM for strategy '{effectiveStrategy}'.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("OllamaQueryTransformationService",
                    $"Error in LLM expansion, falling back to heuristics only: {ex.Message}");
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

        lock (cacheLock)
        {
            if (llmCache.TryGetValue(cacheKey, out var cached))
            {
                cacheOrder.Remove(cacheKey);
                cacheOrder.AddLast(cacheKey);

                logger?.LogInfo("OllamaQueryTransformationService",
                    $"[LLM EXPANSION CACHE HIT] Found {cached.Count} cached variants for query '{query}' ({strategy}).");
                return cached;
            }
        }

        string prompt = strategy switch
        {
            QueryTransformationStrategy.MultiQuery =>
                $"""
                Generate 3 alternative reformulations of the following search query, one per line.
                Constraints:
                - Exactly one query per line, no numbering, no bullet points.
                - Each variant must be semantically distinct (different wording or angle).
                - Do NOT simply reorder words or add trivial variations.
                
                Example Input: "how to connect to SQL database in C#"
                Example Output:
                c# sql database connection string and setup
                connecting c# application to sql server tutorial
                ado.net sql connection example c#
                
                Input: "{query}"
                Output:
                """,
            QueryTransformationStrategy.SubQuery =>
                $"""
                Decompose the following search query into 2-3 simpler, focused sub-queries, one per line.
                Constraints:
                - One sub-query per line, no numbering, no bullet points.
                - Sub-queries should be independently searchable and non-overlapping.
                - Keep proper nouns and technical terms intact.
                
                Example Input: "impact of climate change on polar bear population and arctic ice"
                Example Output:
                effects of climate change on polar bears
                arctic sea ice melting trends
                polar bear population statistics
                
                Input: "{query}"
                Output:
                """,
            QueryTransformationStrategy.HyDE =>
                $"""
                Write a hypothetical passage (2-4 sentences) that a document answering the topic would contain.
                Constraints:
                - Must be factual-sounding, not opinion.
                - Focus on terminology and concepts likely to appear in matching documents.
                - Do not include conversational filler.
                
                Example Input: "benefits of meditation for stress"
                Example Output:
                Mindfulness meditation has been clinically shown to reduce cortisol levels, a primary stress hormone. Regular practice promotes neuroplasticity in the amygdala, reducing anxiety responses. Studies indicate that just 15 minutes of daily meditation can significantly improve overall emotional regulation.
                
                Input: "{query}"
                Output:
                """,
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
            
            string[] words = cleanLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 3) continue;
            
            if (cleanLine.StartsWith("Here are", StringComparison.OrdinalIgnoreCase) ||
                cleanLine.StartsWith("Alternative", StringComparison.OrdinalIgnoreCase) ||
                cleanLine.StartsWith("Query", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            if (ComputeWordJaccard(query, cleanLine) > 0.85)
            {
                continue;
            }

            if (!string.Equals(cleanLine, query, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(cleanLine);
            }
        }

        if (list.Count > 0)
        {
            lock (cacheLock)
            {
                if (!llmCache.ContainsKey(cacheKey))
                {
                    if (llmCache.Count >= MaxCacheEntries)
                    {
                        var oldest = cacheOrder.First;
                        if (oldest != null)
                        {
                            llmCache.TryRemove(oldest.Value, out _);
                            cacheOrder.RemoveFirst();
                        }
                    }
                    llmCache[cacheKey] = list;
                    cacheOrder.AddLast(cacheKey);
                }
            }
        }

        return list;
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

    private static List<string> GenerateZeroShotHydeVariants(string query)
    {
        var list = new List<string> { query };

        string syntheticPassage = $"This document describes and provides specific details about: {query}. It contains technical explanations, key concepts, and related definitions.";
        list.Add(syntheticPassage);

        var multiQuery = GenerateMultiQueryVariants(query);
        foreach (var mq in multiQuery)
        {
            if (!list.Contains(mq, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(mq);
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static double ComputeWordJaccard(string a, string b)
    {
        var setA = new HashSet<string>(a.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        
        if (setA.Count == 0 && setB.Count == 0) return 1.0;
        if (setA.Count == 0 || setB.Count == 0) return 0.0;
        
        int intersection = setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Count();
        int union = setA.Count + setB.Count - intersection;
        
        return (double)intersection / union;
    }
}

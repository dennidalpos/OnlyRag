using System.Collections.Concurrent;
using System.Diagnostics;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class OllamaQueryTransformationService : IQueryTransformationService
{
    private const int MaxCacheEntries = 500;
    private readonly ILlmQueryExpander? llmExpander;
    private readonly ConcurrentDictionary<(string Query, QueryTransformationStrategy Strategy), List<string>> llmCache = new();
    private readonly LinkedList<(string Query, QueryTransformationStrategy Strategy)> cacheOrder = new();
    private readonly object cacheLock = new();

    public OllamaQueryTransformationService(ILlmQueryExpander? llmExpander = null)
    {
        this.llmExpander = llmExpander;
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
            QueryTransformationStrategy.SubQuery => GenerateSubQueryVariants(trimmed),
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
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"LLM query expansion failed; using heuristic variants. {ex.Message}");
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
                $$"""
                Write a hypothetical document passage OR a hypothetical code snippet/class signature that directly answers or implements the following topic.
                Constraints:
                - If the topic is about programming, APIs, or code, output a realistic code snippet, class definition, or function signature with technical comments.
                - If the topic is general, output a factual 2-4 sentence passage.
                - Do not include conversational filler or meta-explanations.
                
                Example Input: "how to implement JWT token authentication in ASP.NET Core Minimal API"
                Example Output:
                // ASP.NET Core Minimal API JWT Authentication implementation
                builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options => {
                        options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true };
                    });
                app.MapPost("/api/login", (UserCredentials creds) => GenerateJwtToken(creds));
                
                Input: "{{query}}"
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

        if (strategy == QueryTransformationStrategy.HyDE)
        {
            string cleanPassage = CleanLine(rawResponse.Trim());
            if (!string.IsNullOrWhiteSpace(cleanPassage) && !string.Equals(cleanPassage, query, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(cleanPassage);
            }
        }
        else
        {
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

    private static List<string> GenerateSubQueryVariants(string query)
    {
        List<string> list = [query];
        string[] delimiters = [" and ", " e ", " vs ", " versus ", " compared to ", " confronta ", " oltre a ", " nonche ", " e anche ", " e ", " - ", " ; ", ", "];

        foreach (string delim in delimiters)
        {
            if (query.Contains(delim, StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = query.Split(new[] { delim }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (string part in parts)
                {
                    if (part.Length >= 4 && !list.Contains(part, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(part);
                    }
                }
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GenerateZeroShotHydeVariants(string query)
    {
        var list = new List<string> { query };

        bool isCodeQuery = query.Contains("class", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("interface", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("function", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("method", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("code", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("api", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("c#", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("typescript", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("sql", StringComparison.OrdinalIgnoreCase);

        if (isCodeQuery)
        {
            string syntheticCode = $"// Hypothetical implementation snippet for: {query}\npublic class HypotheticalCodeImplementation\n{{\n    // Primary method logic and signature for {query}\n    public async Task ExecuteAsync()\n    {{\n    }}\n}}";
            list.Add(syntheticCode);
        }

        string syntheticPassage = $"This document describes and provides specific details about: {query}. It contains technical explanations, key concepts, code definitions, and related signatures.";
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

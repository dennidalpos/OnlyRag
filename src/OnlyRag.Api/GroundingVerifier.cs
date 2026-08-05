using System.Text.RegularExpressions;
using OnlyRag.Core;

namespace OnlyRag.Api;

internal static partial class GroundingVerifier
{
    private const string Abstention = "I cannot provide a document-grounded answer because the retrieved excerpts do not support the requested claims.";

    public static (string Answer, GroundingVerification Verification) Verify(string answer, IReadOnlyList<ChatSource> sources)
    {
        if (sources.Count == 0)
        {
            return (Abstention, new GroundingVerification(false, false, "No retrieved evidence is available.", []));
        }

        List<GroundingClaim> claims = [];
        foreach (string sentence in SplitClaims(answer))
        {
            IReadOnlyList<ChatSource> cited = GetCitedSources(sentence, sources);
            string claimText = CitationRegex().Replace(sentence, string.Empty).Trim();
            if (claimText.Length < 12 || IsAbstention(claimText)) continue;

            bool supported = cited.Any(source => HasMeaningfulOverlap(claimText, source.Snippet));
            claims.Add(new GroundingClaim(
                claimText,
                supported,
                cited.Where(source => HasMeaningfulOverlap(claimText, source.Snippet)).Select(source => source.ChunkId).Distinct().ToArray(),
                supported ? null : cited.Count == 0 ? "A factual claim is missing a valid inline citation." : "The cited excerpt does not support this claim."));
        }

        bool hasConflicts = HasConflictingEvidence(sources);
        bool grounded = claims.Count > 0 && claims.All(claim => claim.IsSupported);
        if (!grounded)
        {
            string reason = claims.Count == 0
                ? "The answer contains no verifiable cited claims."
                : "One or more claims are unsupported by their cited excerpts.";
            return (Abstention, new GroundingVerification(false, hasConflicts, reason, claims));
        }

        return (answer, new GroundingVerification(true, hasConflicts, null, claims));
    }

    private static IEnumerable<string> SplitClaims(string answer) => Regex.Split(answer, @"\r?\n+")
        .Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<ChatSource> GetCitedSources(string sentence, IReadOnlyList<ChatSource> sources) =>
        sources.Where(source => sentence.Contains($"Source: {source.DocumentName}", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static bool HasMeaningfulOverlap(string claim, string snippet)
    {
        HashSet<string> claimTerms = Tokenize(claim).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> sourceTerms = Tokenize(snippet).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return claimTerms.Intersect(sourceTerms, StringComparer.OrdinalIgnoreCase).Any();
    }

    private static IEnumerable<string> Tokenize(string text) => Regex.Matches(text, @"[\p{L}\p{N}]{3,}")
        .Select(match => match.Value).Where(word => word is not ("source" or "document" or "pagina" or "page"));

    private static bool IsAbstention(string text) => text.Contains("cannot provide", StringComparison.OrdinalIgnoreCase)
        || text.Contains("informazioni sufficienti", StringComparison.OrdinalIgnoreCase)
        || text.Contains("non posso", StringComparison.OrdinalIgnoreCase);

    private static bool HasConflictingEvidence(IReadOnlyList<ChatSource> sources)
    {
        for (int index = 0; index < sources.Count; index++)
        {
            bool leftNegated = sources[index].Snippet.Contains(" not ", StringComparison.OrdinalIgnoreCase)
                || sources[index].Snippet.Contains(" non ", StringComparison.OrdinalIgnoreCase);
            HashSet<string> leftTerms = Tokenize(sources[index].Snippet).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int candidate = index + 1; candidate < sources.Count; candidate++)
            {
                bool rightNegated = sources[candidate].Snippet.Contains(" not ", StringComparison.OrdinalIgnoreCase)
                    || sources[candidate].Snippet.Contains(" non ", StringComparison.OrdinalIgnoreCase);
                if (leftNegated != rightNegated && leftTerms.Intersect(Tokenize(sources[candidate].Snippet), StringComparer.OrdinalIgnoreCase).Count() >= 3)
                {
                    return true;
                }
            }
        }
        return false;
    }

    [GeneratedRegex(@"\*\*\(Source:\s*[^)]+\)\*\*|\(Source:\s*[^)]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex CitationRegex();
}

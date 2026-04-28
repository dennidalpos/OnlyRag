using System.Text.RegularExpressions;

namespace OnlyRag.Api;

internal static class TranslationOutputValidator
{
    private static readonly Regex PlaceholderRegex = new(
        @"(\{\{[^}]+\}\}|\{[A-Za-z0-9_.:-]+\}|\$\{[^}]+\}|%[A-Za-z0-9_.:-]+%|<%=?\s*[^%]+%>)",
        RegexOptions.Compiled);

    public static TranslationValidationResult Validate(string sourceText, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return TranslationValidationResult.Fail("Output traduzione vuoto.");
        }

        List<string> warnings = [];
        foreach (string placeholder in ExtractDistinct(PlaceholderRegex, sourceText))
        {
            if (!translatedText.Contains(placeholder, StringComparison.Ordinal))
            {
                warnings.Add($"Placeholder mancante: {placeholder}");
            }
        }

        return warnings.Count == 0
            ? TranslationValidationResult.Success()
            : TranslationValidationResult.Fail(string.Join("; ", warnings));
    }

    private static IReadOnlyList<string> ExtractDistinct(Regex regex, string text)
    {
        return regex.Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed record TranslationValidationResult(bool IsValid, string? Warnings)
{
    public static TranslationValidationResult Success()
    {
        return new TranslationValidationResult(true, null);
    }

    public static TranslationValidationResult Fail(string warnings)
    {
        return new TranslationValidationResult(false, warnings);
    }
}

internal sealed class TranslationValidationException : Exception
{
    public TranslationValidationException(string title, string message)
        : base(message)
    {
        Title = title;
    }

    public string Title { get; }
}

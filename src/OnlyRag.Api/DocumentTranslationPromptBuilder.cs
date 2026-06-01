using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

internal static class DocumentTranslationPromptBuilder
{
    public static IReadOnlyList<OllamaChatMessage> BuildMessages(
        string targetLanguage,
        StoredTranslationUnit unit)
    {
        string language = NormalizeLanguage(targetLanguage);
        return
        [
            new OllamaChatMessage(
                "system",
                $"""
                You are a document translation engine.
                Translate only the text inside <source_text> tags.
                {GetUnitKindInstruction(unit.UnitKind)}
                Preserve numbers, dates, codes, placeholders, line breaks, indentation, and list markers (-, *, 1.) exactly as they appear.
                Do not add explanations, comments, markdown fences, XML tags, headings, or any extra text.
                Return only the translated text with no wrapper or delimiter.
                """),
            new OllamaChatMessage(
                "user",
                $"""
                Target language: {language}
                Unit kind: {unit.UnitKind}
                Page/unit: {unit.PageNumber?.ToString() ?? "n/a"}

                <source_text>
                {unit.SourceText}
                </source_text>
                """)
        ];
    }

    public static IReadOnlyList<OllamaChatMessage> BuildRepairMessages(
        string targetLanguage,
        StoredTranslationUnit unit,
        string failedOutput,
        string validationWarnings)
    {
        string language = NormalizeLanguage(targetLanguage);
        return
        [
            new OllamaChatMessage(
                "system",
                $"""
                You are repairing a failed document translation unit.
                Return only the corrected translation in {language}.
                Preserve every placeholder, code token, number, date, line break, indentation, and list marker exactly as in the source.
                Do not add explanations, comments, markdown fences, XML tags, headings, or wrappers.
                """),
            new OllamaChatMessage(
                "user",
                $"""
                Validation failure: {validationWarnings}
                Unit kind: {unit.UnitKind}
                Page/unit: {unit.PageNumber?.ToString() ?? "n/a"}

                Source text:
                <source_text>
                {unit.SourceText}
                </source_text>

                Previous failed output:
                <failed_output>
                {failedOutput}
                </failed_output>
                """)
        ];
    }

    private static string GetUnitKindInstruction(string? unitKind) => unitKind switch
    {
        "table-cell" => "This is a single table cell: translate the content only, preserve short values like numbers or codes verbatim, do not add surrounding punctuation or sentence structure.",
        "textbox" => "This is a text box: preserve all line breaks and any capitalization style of the original.",
        _ => "This is a paragraph: preserve paragraph structure, bold/italic markers if present, and list indentation."
    };

    public static string NormalizeLanguage(string targetLanguage)
    {
        return SupportedTargetLanguages.TryGetValue(targetLanguage.Trim(), out string? language)
            ? language
            : throw new TranslationValidationException(
                "Lingua target non supportata",
                $"La lingua target '{targetLanguage}' non e supportata.");
    }

    public static IReadOnlyDictionary<string, string> SupportedTargetLanguages { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["English"] = "English",
            ["Spanish"] = "Spanish",
            ["French"] = "French",
            ["German"] = "German",
            ["Italian"] = "Italian",
            ["Portuguese"] = "Portuguese",
            ["Dutch"] = "Dutch",
            ["Polish"] = "Polish",
            ["Romanian"] = "Romanian",
            ["Chinese"] = "Chinese",
            ["Japanese"] = "Japanese",
            ["Korean"] = "Korean",
            ["Arabic"] = "Arabic",
            ["Russian"] = "Russian"
        };
}

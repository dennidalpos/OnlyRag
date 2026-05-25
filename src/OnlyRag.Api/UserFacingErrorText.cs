using System.Text;
using System.Text.RegularExpressions;

namespace OnlyRag.Api;

public static class UserFacingErrorText
{
    private const int MaxUserFacingErrorLength = 600;
    private static readonly Regex FileUriPattern = new(@"file:///[^\s)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlPattern = new(@"https?://[^\s)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WindowsPathPattern = new(@"(?<![\w.])[A-Za-z]:\\(?:[^\s\\/:*?""<>|]+\\)*[^\s\\/:*?""<>|]*", RegexOptions.Compiled);
    private static readonly Regex UncPathPattern = new(@"\\\\[^\s\\/:*?""<>|]+\\(?:[^\s\\/:*?""<>|]+\\)*[^\s\\/:*?""<>|]*", RegexOptions.Compiled);
    private static readonly Regex IpEndpointPattern = new(@"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b", RegexOptions.Compiled);
    private static readonly Regex CredentialPattern = new(
        @"\b(password|passwd|pwd|secret|token|api[-_ ]?key|apiToken|sessionToken|access_token|refresh_token|id_token|clientSecret|client_secret|authorization)\b\s*[:=]\s*(?:bearer\s+)?([^\s,;]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BearerTokenPattern = new(
        @"\bbearer\s+[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string FromExternalDetail(string? detail, string fallback)
    {
        string normalized = Normalize(detail);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        normalized = FileUriPattern.Replace(normalized, "[percorso locale]");
        normalized = WindowsPathPattern.Replace(normalized, "[percorso locale]");
        normalized = UncPathPattern.Replace(normalized, "[percorso locale]");
        normalized = UrlPattern.Replace(normalized, "[endpoint esterno]");
        normalized = CredentialPattern.Replace(normalized, "$1=[segreto]");
        normalized = BearerTokenPattern.Replace(normalized, "Bearer [segreto]");
        normalized = IpEndpointPattern.Replace(normalized, "[host]");
        normalized = CollapseWhitespace(normalized);

        if (normalized.Length > MaxUserFacingErrorLength)
        {
            normalized = normalized[..MaxUserFacingErrorLength].TrimEnd() + "...";
        }

        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    public static string StartupFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string detail = FromExternalDetail(
            exception.Message,
            "Il backend locale non e stato avviato. Controlla i log locali per i dettagli tecnici.");
        return $"Il backend locale non e stato avviato. Dettaglio: {detail}";
    }

    private static string Normalize(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        StringBuilder builder = new(detail.Length);
        foreach (char character in detail)
        {
            builder.Append(char.IsControl(character) && character is not ('\r' or '\n' or '\t')
                ? ' '
                : character);
        }

        return builder.ToString().Trim();
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

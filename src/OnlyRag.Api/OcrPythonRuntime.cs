using System.Text.RegularExpressions;

namespace OnlyRag.Api;

internal static class OcrPythonRuntime
{
    private static readonly int[] SupportedMinorVersions = [13, 12, 11, 10];

    public static IEnumerable<OcrPythonCommand> ResolveCandidates(Func<string, string?> executableResolver)
    {
        string? python = executableResolver("python");
        if (python is not null)
        {
            yield return new OcrPythonCommand(python, []);
        }

        string? py = executableResolver("py");
        if (py is null)
        {
            yield break;
        }

        foreach (int minor in SupportedMinorVersions)
        {
            yield return new OcrPythonCommand(py, [$"-3.{minor}"]);
        }
    }

    public static Version? ParseVersion(string text)
    {
        Match match = Regex.Match(text, @"(\d+)\.(\d+)\.(\d+)");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : null;
    }

    public static bool IsSupportedVersion(Version version)
    {
        return version.Major == 3 && SupportedMinorVersions.Contains(version.Minor);
    }

    public static string GetVersionText(LocalProcessResult result)
    {
        return (string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput).Trim();
    }
}

internal sealed record OcrPythonCommand(string FileName, IReadOnlyList<string> PrefixArguments)
{
    public string[] WithArguments(IReadOnlyList<string> arguments)
    {
        return [.. PrefixArguments, .. arguments];
    }
}

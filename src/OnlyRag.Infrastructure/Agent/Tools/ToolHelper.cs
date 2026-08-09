using System.Text;
using System.Text.Json;

namespace OnlyRag.Infrastructure.Agent.Tools;

public static class ToolHelper
{
    public static string? GetArgString(JsonElement root, params string[] propertyNames)
    {
        foreach (var prop in propertyNames)
        {
            if (root.TryGetProperty(prop, out var elem))
            {
                string? parsed = ExtractStringValue(elem);
                if (parsed is not null) return parsed;
            }
        }
        foreach (var prop in root.EnumerateObject())
        {
            if (propertyNames.Any(p => p.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                string? parsed = ExtractStringValue(prop.Value);
                if (parsed is not null) return parsed;
            }
        }
        return null;
    }

    private static string? ExtractStringValue(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.String)
        {
            return elem.GetString();
        }
        if (elem.ValueKind == JsonValueKind.Array && elem.GetArrayLength() > 0)
        {
            var first = elem[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                return first.GetString();
            }
        }
        if (elem.ValueKind == JsonValueKind.Number)
        {
            return elem.GetRawText();
        }
        return null;
    }

    public static int? GetArgInt(JsonElement root, params string[] propertyNames)
    {
        foreach (var prop in propertyNames)
        {
            if (root.TryGetProperty(prop, out var elem))
            {
                if (elem.ValueKind == JsonValueKind.Number)
                {
                    return elem.GetInt32();
                }
                if (elem.ValueKind == JsonValueKind.String && int.TryParse(elem.GetString(), out int parsed))
                {
                    return parsed;
                }
            }
        }
        return null;
    }

    public static string ResolveSafePath(string rootPath, string relativePath)
    {
        string root = Path.GetFullPath(rootPath);
        string cleanedRelative = (relativePath ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(cleanedRelative) && cleanedRelative.Contains(',') && !File.Exists(Path.Combine(root, cleanedRelative)))
        {
            cleanedRelative = cleanedRelative.Split(',')[0].Trim();
        }

        string target;
        if (Path.IsPathRooted(cleanedRelative))
        {
            target = Path.GetFullPath(cleanedRelative);
        }
        else
        {
            target = Path.GetFullPath(Path.Combine(root, cleanedRelative.TrimStart('/', '\\')));
        }

        if (!IsPathWithinRoot(root, target))
        {
            throw new UnauthorizedAccessException($"Path Traversal blocked: the path '{relativePath}' is outside the workspace folder '{rootPath}'.");
        }

        return target;
    }

    private static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        char boundaryChar = normalizedCandidate[normalizedRoot.Length];
        return boundaryChar == Path.DirectorySeparatorChar || boundaryChar == Path.AltDirectorySeparatorChar;
    }

    public static string ResolveSafePathWithSmartFallback(string rootPath, string relativePath, out string resolvedRelativePath)
    {
        string safePath = ResolveSafePath(rootPath, relativePath);
        resolvedRelativePath = (relativePath ?? "").Trim().Replace('\\', '/');

        if (File.Exists(safePath) || Directory.Exists(safePath))
        {
            return safePath;
        }

        string? fileName = Path.GetFileName(relativePath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                var candidates = Directory.EnumerateFiles(rootPath, fileName, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\node_modules\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                    .ToList();

                if (candidates.Count > 0)
                {
                    string candidate = candidates[0];
                    resolvedRelativePath = Path.GetRelativePath(rootPath, candidate).Replace('\\', '/');
                    return candidate;
                }
            }
            catch
            {
                // Fallback safe
            }
        }

        return safePath;
    }

    public static string GetNearbyFileSuggestions(string rootPath, string relativePath)
    {
        string? fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

        string ext = Path.GetExtension(fileName);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(nameWithoutExt)) return string.Empty;

        try
        {
            var suggestions = Directory.EnumerateFiles(rootPath, string.IsNullOrEmpty(ext) ? "*.*" : $"*{ext}", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\node_modules\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                .Select(f => Path.GetRelativePath(rootPath, f).Replace('\\', '/'))
                .Where(rel => rel.Contains(nameWithoutExt, StringComparison.OrdinalIgnoreCase) || nameWithoutExt.Contains(Path.GetFileNameWithoutExtension(rel), StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            if (suggestions.Count > 0)
            {
                return $"\n[REAL FILE SUGGESTION] Real files with similar name or extension found in the workspace:\n- " + string.Join("\n- ", suggestions);
            }
        }
        catch
        {
            // Safe fallback
        }

        return string.Empty;
    }

    public static string GenerateUnifiedDiffPatch(string relativePath, string oldContent, string newContent)
    {
        string relPath = (relativePath ?? "").Replace('\\', '/');
        string[] oldLines = (oldContent ?? "").Replace("\r\n", "\n").Split('\n');
        string[] newLines = (newContent ?? "").Replace("\r\n", "\n").Split('\n');

        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{relPath}");
        sb.AppendLine($"+++ b/{relPath}");

        int max = Math.Max(oldLines.Length, newLines.Length);
        int diffCount = 0;

        for (int i = 0; i < max; i++)
        {
            string? oldL = i < oldLines.Length ? oldLines[i] : null;
            string? newL = i < newLines.Length ? newLines[i] : null;

            if (oldL != newL)
            {
                if (oldL != null)
                {
                    sb.AppendLine($"- {oldL}");
                    diffCount++;
                }
                if (newL != null)
                {
                    sb.AppendLine($"+ {newL}");
                    diffCount++;
                }
            }
        }

        return diffCount > 0 ? sb.ToString() : string.Empty;
    }
}

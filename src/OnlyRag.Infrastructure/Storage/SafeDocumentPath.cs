namespace OnlyRag.Infrastructure.Storage;

public static class SafeDocumentPath
{
    public static string NormalizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string normalized = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Il nome file non e valido.", nameof(fileName));
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Il nome file contiene caratteri non validi.", nameof(fileName));
        }

        return normalized;
    }

    public static string NormalizeFileExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        extension = extension.Trim().ToLowerInvariant();
        if (extension.Length > 16)
        {
            return string.Empty;
        }

        for (int index = 1; index < extension.Length; index++)
        {
            if (!char.IsLetterOrDigit(extension[index]))
            {
                return string.Empty;
            }
        }

        return extension;
    }

    public static string ResolveWithinRoot(string rootDirectory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (fileName.Contains('\0') || fileName.Contains("%2e%2e", StringComparison.OrdinalIgnoreCase) || fileName.Contains("::DATA", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Il nome file contiene sequenze non consentite.", nameof(fileName));
        }

        string normalizedFileName = NormalizeFileName(fileName);

        string rootFullPath = EnsureTrailingSeparator(Path.GetFullPath(rootDirectory));
        string candidate = Path.GetFullPath(Path.Combine(rootFullPath, normalizedFileName));

        if (!candidate.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Il path risolto esce dalla directory documenti consentita.");
        }

        return candidate;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}

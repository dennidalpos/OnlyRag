using System.Security.Cryptography;
using System.Text;

namespace OnlyRag.Infrastructure.Ocr;

public static class OcrCacheKey
{
    public static string Create(
        string pageHash,
        string engineName,
        string engineVersion,
        string language,
        string preprocessVersion,
        string settingsSignature = "")
    {
        string raw = string.Join(
            '|',
            pageHash.Trim().ToLowerInvariant(),
            engineName.Trim(),
            engineVersion.Trim(),
            language.Trim().ToLowerInvariant(),
            preprocessVersion.Trim(),
            settingsSignature.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}

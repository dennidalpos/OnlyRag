using OnlyRag.Core;

namespace OnlyRag.App;

public sealed record BackendWebSettings(
    bool IsRunning,
    string? BaseUrl,
    string? ApiToken,
    string ApiTokenHeaderName,
    string? ErrorMessage)
{
    public static BackendWebSettings Offline(string errorMessage)
    {
        return new BackendWebSettings(false, null, null, OnlyRagApiHeaders.SessionTokenHeaderName, errorMessage);
    }
}

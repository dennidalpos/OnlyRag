namespace OnlyRag.App;

public sealed record BackendWebSettings(bool IsRunning, string? BaseUrl, string? ErrorMessage)
{
    public static BackendWebSettings Offline(string errorMessage)
    {
        return new BackendWebSettings(false, null, errorMessage);
    }
}

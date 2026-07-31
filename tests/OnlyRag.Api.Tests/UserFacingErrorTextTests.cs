namespace OnlyRag.Api.Tests;

public sealed class UserFacingErrorTextTests
{
    [Fact]
    public void FromExternalDetail_RedactsLocalPathsUrlsAndHosts()
    {
        string sensitive = @"failed at C:\Users\Alice\AppData\Local\OnlyRag\ocr-python\.venv\Scripts\python.exe while contacting http://192.168.1.20:11434/api with file:///C:/Users/Alice/secret.txt";

        string message = UserFacingErrorText.FromExternalDetail(
            sensitive,
            "Errore tecnico.");

        Assert.Contains("[local path]", message, StringComparison.Ordinal);
        Assert.Contains("[external endpoint]", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.20", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.txt", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromExternalDetail_RedactsCredentialLikeFragments()
    {
        string sensitive = "request failed with password=dummy-value Authorization: Bearer dummy-token api_key=dummy-key apiToken=dummy-api-token sessionToken=dummy-session-token access_token=dummy-access-token clientSecret=dummy-client-secret bearer loose-token";

        string message = UserFacingErrorText.FromExternalDetail(
            sensitive,
            "Technical error.");

        Assert.Contains("password=[redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization=[redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bearer [redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_key=[redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("apiToken=[redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sessionToken=[redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_token=[redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clientSecret=[redacted]", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-value", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-token", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-key", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-api-token", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-session-token", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-access-token", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-client-secret", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupFailure_UsesGenericUserFacingPrefix()
    {
        InvalidOperationException exception = new(@"qdrant.exe failed from C:\Users\Alice\AppData\Local\OnlyRag\qdrant\qdrant.exe");

        string message = UserFacingErrorText.StartupFailure(exception);

        Assert.StartsWith("The local backend failed to start.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qdrant.exe failed from C:", message, StringComparison.OrdinalIgnoreCase);
    }
}

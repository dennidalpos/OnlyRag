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

        Assert.Contains("[percorso locale]", message, StringComparison.Ordinal);
        Assert.Contains("[endpoint esterno]", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.20", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.txt", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromExternalDetail_RedactsCredentialLikeFragments()
    {
        string sensitive = "request failed with password=dummy-value Authorization: Bearer dummy-token api_key=dummy-key bearer loose-token";

        string message = UserFacingErrorText.FromExternalDetail(
            sensitive,
            "Errore tecnico.");

        Assert.Contains("password=[segreto]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization=[segreto]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bearer [segreto]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_key=[segreto]", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-value", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-token", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dummy-key", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupFailure_UsesGenericUserFacingPrefix()
    {
        InvalidOperationException exception = new(@"qdrant.exe failed from C:\Users\Alice\AppData\Local\OnlyRag\qdrant\qdrant.exe");

        string message = UserFacingErrorText.StartupFailure(exception);

        Assert.StartsWith("Il backend locale non e stato avviato.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qdrant.exe failed from C:", message, StringComparison.OrdinalIgnoreCase);
    }
}

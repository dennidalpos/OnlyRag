namespace OnlyRag.Core;

public sealed record AppStoragePaths(
    string DataRoot,
    string DataDirectory,
    string DatabasePath,
    string DocumentsRoot,
    string DocumentOriginalsDirectory,
    string DocumentRendersDirectory,
    string DocumentOcrCacheDirectory,
    string DocumentExportsDirectory,
    string LogsDirectory,
    string WebView2UserDataDirectory,
    string TempDirectory)
{
    public const string ProductName = "OnlyRag";

    public static AppStoragePaths FromLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return FromRoot(Path.Combine(localAppData, ProductName));
    }

    public static AppStoragePaths FromRoot(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        string normalizedRoot = Path.GetFullPath(dataRoot);
        string dataDirectory = Path.Combine(normalizedRoot, "data");
        string documentsRoot = Path.Combine(normalizedRoot, "documents");

        return new AppStoragePaths(
            normalizedRoot,
            dataDirectory,
            Path.Combine(dataDirectory, "onlyrag.db"),
            documentsRoot,
            Path.Combine(documentsRoot, "originals"),
            Path.Combine(documentsRoot, "renders"),
            Path.Combine(documentsRoot, "ocr-cache"),
            Path.Combine(documentsRoot, "exports"),
            Path.Combine(normalizedRoot, "logs"),
            Path.Combine(normalizedRoot, "webview2"),
            Path.Combine(normalizedRoot, "temp"));
    }

    public IEnumerable<string> EnumerateRequiredDirectories()
    {
        yield return DataRoot;
        yield return DataDirectory;
        yield return DocumentOriginalsDirectory;
        yield return DocumentRendersDirectory;
        yield return DocumentOcrCacheDirectory;
        yield return DocumentExportsDirectory;
        yield return LogsDirectory;
        yield return WebView2UserDataDirectory;
        yield return TempDirectory;
    }
}

namespace OnlyRag.Core;

public sealed record AppStoragePaths(
    string DataRoot,
    string DataDirectory,
    string DatabasePath,
    string JobQueueDatabasePath,
    string DocumentsRoot,
    string DocumentOriginalsDirectory,
    string DocumentRendersDirectory,
    string DocumentOcrCacheDirectory,
    string DocumentExportsDirectory,
    string LogsDirectory)
{
    public const string ProductName = "OnlyRag";

    public static AppStoragePaths FromLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return FromRoot(Path.Combine(localAppData, ProductName));
    }

    public static AppStoragePaths FromRoot(string dataRoot)
    {
        string dataDirectory = Path.Combine(dataRoot, "data");
        string documentsRoot = Path.Combine(dataRoot, "documents");

        return new AppStoragePaths(
            dataRoot,
            dataDirectory,
            Path.Combine(dataDirectory, "onlyrag.db"),
            Path.Combine(dataDirectory, "jobs.db"),
            documentsRoot,
            Path.Combine(documentsRoot, "originals"),
            Path.Combine(documentsRoot, "renders"),
            Path.Combine(documentsRoot, "ocr-cache"),
            Path.Combine(documentsRoot, "exports"),
            Path.Combine(dataRoot, "logs"));
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
    }
}

namespace OnlyRag.Infrastructure.Storage;

internal static class LocalRuntimeDirectoryPreparer
{
    public static void EnsureDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                "Impossibile preparare una directory runtime di OnlyRag. " +
                $"Percorso: {directory}. " +
                "Verifica che il percorso non sia un file e che l'utente corrente abbia permessi di lettura e scrittura.",
                ex);
        }
    }
}

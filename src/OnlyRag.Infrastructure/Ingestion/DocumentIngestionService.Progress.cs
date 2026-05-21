namespace OnlyRag.Infrastructure.Ingestion;

public sealed partial class DocumentIngestionService
{
    private static int CalculateProgress(int completed, int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(completed * 100d / total), 0, 99);
    }
}

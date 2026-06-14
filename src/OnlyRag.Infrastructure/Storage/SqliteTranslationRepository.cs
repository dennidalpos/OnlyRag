using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteTranslationRepository : ITranslationRepository
{
    private const int MaxUnitCharacters = 6000;
    private static readonly Regex CellRegex = new(@"\[[A-Za-z]{1,4}\d{1,7}\]\s*[^|]+", RegexOptions.Compiled);
    private static readonly Regex PresentationLineRegex = new(@"^(Textbox|Note)\s+(\d+):\s+.+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteTranslationRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }


    public async Task<StoredTranslation?> GetAsync(long translationId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildTranslationSelect("WHERE t.id = $translationId");
        command.AddParameter("$translationId", translationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTranslation(reader) : null;
    }

    public async Task<IReadOnlyList<StoredTranslation>> ListByDocumentAsync(
        long documentId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildTranslationSelect("WHERE t.document_id = $documentId ORDER BY t.created_at_utc DESC, t.id DESC");
        command.AddParameter("$documentId", documentId);

        List<StoredTranslation> translations = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            translations.Add(ReadTranslation(reader));
        }

        return translations;
    }

    public async Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsAsync(
        long translationId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect("WHERE translation_id = $translationId ORDER BY unit_index ASC");
        command.AddParameter("$translationId", translationId);

        List<StoredTranslationUnit> units = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(ReadUnit(reader));
        }

        return units;
    }

    public async Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsPreviewAsync(
        long translationId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect(
            """
            WHERE translation_id = $translationId
            ORDER BY unit_index ASC
            LIMIT $take
            """);
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$take", Math.Clamp(take, 1, 200));

        List<StoredTranslationUnit> units = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(ReadUnit(reader));
        }

        return units;
    }

    public async Task<IReadOnlyList<int>> ListUnitPagesAsync(
        long translationId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT COALESCE(page_number, unit_index + 1)
            FROM translation_units
            WHERE translation_id = $translationId
            ORDER BY COALESCE(page_number, unit_index + 1) ASC;
            """;
        command.AddParameter("$translationId", translationId);

        List<int> pages = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pages.Add(reader.GetInt32(0));
        }

        return pages;
    }

    public async Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsByPageAsync(
        long translationId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect(
            """
            WHERE translation_id = $translationId
              AND COALESCE(page_number, unit_index + 1) = $pageNumber
            ORDER BY unit_index ASC
            """);
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$pageNumber", pageNumber);

        List<StoredTranslationUnit> units = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(ReadUnit(reader));
        }

        return units;
    }

    public async Task<StoredTranslationUnit?> GetUnitAsync(
        long translationId,
        long unitId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect("WHERE translation_id = $translationId AND id = $unitId");
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$unitId", unitId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUnit(reader) : null;
    }

    public async Task<StoredTranslationUnit?> GetNextPendingUnitAsync(
        long translationId,
        int afterUnitIndex,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect(
            """
            WHERE translation_id = $translationId
              AND unit_index >= $afterUnitIndex
              AND status IN ('Pending', 'Failed')
            ORDER BY unit_index ASC
            LIMIT 1
            """);
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$afterUnitIndex", Math.Max(0, afterUnitIndex));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUnit(reader) : null;
    }

}

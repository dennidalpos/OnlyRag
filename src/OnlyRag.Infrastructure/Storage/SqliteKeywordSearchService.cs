using Microsoft.Data.Sqlite;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteKeywordSearchService : IKeywordSearchService
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteKeywordSearchService(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<KeywordSearchResponse> SearchAsync(
        string query,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        string[] terms = ExtractTerms(query).ToArray();
        if (string.IsNullOrWhiteSpace(query) || documentIds.Count == 0 || limit <= 0 || terms.Length == 0)
        {
            return new KeywordSearchResponse([], "none");
        }

        string ftsQuery = BuildFtsQuery(terms);
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        if (await TableExistsAsync(connection, "chunks_fts", cancellationToken))
        {
            bool usesFts5 = await TableSqlContainsAsync(connection, "chunks_fts", "USING fts5", cancellationToken);
            try
            {
                IReadOnlyList<KeywordSearchResult> ftsResults = await SearchFtsAsync(
                    connection,
                    ftsQuery,
                    documentIds,
                    limit,
                    cancellationToken);
                return new KeywordSearchResponse(ftsResults, usesFts5 ? "SQLite FTS5" : "SQLite FTS indexed fallback");
            }
            catch (SqliteException)
            {
                IReadOnlyList<KeywordSearchResult> fallbackResults = await SearchFtsWithoutRankAsync(
                    connection,
                    ftsQuery,
                    documentIds,
                    limit,
                    cancellationToken);
                return new KeywordSearchResponse(fallbackResults, "SQLite FTS indexed fallback");
            }
        }

        return new KeywordSearchResponse([], "SQLite keyword search unavailable");
    }

    private static async Task<IReadOnlyList<KeywordSearchResult>> SearchFtsAsync(
        SqliteConnection connection,
        string ftsQuery,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        string documentParameters = command.AddInParameters("$doc", documentIds.Distinct().ToArray());
        command.CommandText =
            $$"""
            SELECT c.id, c.document_id, c.chunk_index, fts.rank
            FROM (
                SELECT chunk_id, bm25(chunks_fts) AS rank
                FROM chunks_fts
                WHERE chunks_fts MATCH $query
            ) AS fts
            INNER JOIN chunks AS c ON c.id = fts.chunk_id
            WHERE c.document_id IN ({{documentParameters}})
              AND LENGTH(TRIM(c.content)) > 0
            ORDER BY fts.rank ASC, c.id ASC
            LIMIT $limit;
            """;
        command.AddParameter("$query", ftsQuery);
        command.AddParameter("$limit", Math.Max(1, limit));

        return await ReadRankedResultsAsync(command, limit, cancellationToken);
    }

    private static async Task<IReadOnlyList<KeywordSearchResult>> SearchFtsWithoutRankAsync(
        SqliteConnection connection,
        string ftsQuery,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        string documentParameters = command.AddInParameters("$doc", documentIds.Distinct().ToArray());
        command.CommandText =
            $$"""
            SELECT c.id, c.document_id, c.chunk_index
            FROM (
                SELECT chunk_id
                FROM chunks_fts
                WHERE chunks_fts MATCH $query
            ) AS fts
            INNER JOIN chunks AS c ON c.id = fts.chunk_id
            WHERE c.document_id IN ({{documentParameters}})
              AND LENGTH(TRIM(c.content)) > 0
            ORDER BY c.id ASC
            LIMIT $limit;
            """;
        command.AddParameter("$query", ftsQuery);
        command.AddParameter("$limit", Math.Max(1, limit));

        List<KeywordSearchResult> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            double score = Math.Max(1, limit - results.Count);
            results.Add(new KeywordSearchResult(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                score));
        }

        return results;
    }

    private static async Task<IReadOnlyList<KeywordSearchResult>> ReadRankedResultsAsync(
        SqliteCommand command,
        int limit,
        CancellationToken cancellationToken)
    {
        List<KeywordSearchResult> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            double score = ConvertBm25RankToScore(reader.GetDouble(3), limit, results.Count);
            results.Add(new KeywordSearchResult(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                score));
        }

        return results;
    }

    private static double ConvertBm25RankToScore(double rank, int limit, int resultIndex)
    {
        if (!double.IsFinite(rank))
        {
            return Math.Max(1, limit - resultIndex);
        }

        return rank < 0d
            ? -rank
            : 1d / (1d + rank);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.AddParameter("$name", tableName);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null;
    }

    private static async Task<bool> TableSqlContainsAsync(
        SqliteConnection connection,
        string tableName,
        string expectedText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.AddParameter("$name", tableName);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string sql
            && sql.Contains(expectedText, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFtsQuery(IEnumerable<string> terms)
    {
        return string.Join(" OR ", terms.Select(term => $"\"{term.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
    }

    private static IEnumerable<string> ExtractTerms(string query)
    {
        return query
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Trim('"', '\'', ',', ';', ':', '(', ')', '[', ']', '{', '}'))
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12);
    }
}

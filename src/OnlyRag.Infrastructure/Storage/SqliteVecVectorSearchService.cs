using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteVecVectorSearchService : IVectorSearchService
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteVecVectorSearchService(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public string BackendName => "sqlite-vec (cosine, SQLite extension, persistent BLOB storage)";

    public int MaxSearchableVectors => int.MaxValue;

    public bool IsVectorStoragePersistent => true;

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string model,
        IReadOnlyList<float> queryVector,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (queryVector.Count == 0 || documentIds.Count == 0 || limit <= 0)
        {
            return [];
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        LoadExtension(connection);

        string[] documentParameters = documentIds
            .Select((_, index) => $"$documentId{index}")
            .ToArray();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
                c.id,
                c.document_id,
                c.chunk_index,
                1.0 - vec_distance_cosine(e.vector_blob, $queryVector) AS score
            FROM embeddings AS e
            INNER JOIN chunks AS c ON c.id = e.chunk_id
            WHERE e.model = $model
              AND e.content_hash = c.content_hash
              AND e.dimensions = $dimensions
              AND c.document_id IN ({string.Join(", ", documentParameters)})
            ORDER BY vec_distance_cosine(e.vector_blob, $queryVector) ASC, c.id ASC
            LIMIT $limit;
            """;
        command.AddParameter("$model", model);
        command.AddParameter("$queryVector", SqliteVectorBlob.Serialize(queryVector));
        command.AddParameter("$dimensions", queryVector.Count);
        command.AddParameter("$limit", Math.Max(1, limit));

        int parameterIndex = 0;
        foreach (long documentId in documentIds)
        {
            command.AddParameter(documentParameters[parameterIndex], documentId);
            parameterIndex++;
        }

        List<VectorSearchResult> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new VectorSearchResult(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetDouble(3)));
        }

        return results;
    }

    internal static void LoadExtension(SqliteConnection connection)
    {
        try
        {
            connection.LoadExtension("vec0");
        }
        catch (SqliteException ex)
        {
            string? nativePath = ResolveNativeExtensionPath();
            if (nativePath is not null)
            {
                try
                {
                    connection.LoadExtension(nativePath);
                    return;
                }
                catch (SqliteException nested)
                {
                    throw new InvalidOperationException(
                        $"sqlite-vec extension found but could not be loaded from '{nativePath}': {nested.Message}",
                        nested);
                }
            }

            throw new InvalidOperationException(
                $"sqlite-vec extension is not available. Ensure the sqlite-vec NuGet native asset is packaged for Windows x64. Detail: {ex.Message}",
                ex);
        }
    }

    private static string? ResolveNativeExtensionPath()
    {
        string fileName = OperatingSystem.IsWindows()
            ? "vec0.dll"
            : OperatingSystem.IsMacOS()
                ? "vec0.dylib"
                : "vec0.so";
        string runtime = OperatingSystem.IsWindows()
            ? "win-x64"
            : OperatingSystem.IsMacOS()
                ? "osx-x64"
                : "linux-x64";

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", runtime, "native", fileName)
        ];

        return candidates.FirstOrDefault(path => File.Exists(path));
    }
}

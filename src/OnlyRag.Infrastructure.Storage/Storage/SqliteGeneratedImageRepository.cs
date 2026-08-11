using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteGeneratedImageRepository : IGeneratedImageRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteGeneratedImageRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<GeneratedImage> CreateAsync(
        GeneratedImage image,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO generated_images(
                provider,
                prompt,
                negative_prompt,
                model,
                width,
                height,
                steps,
                batch_size,
                seed,
                file_name,
                relative_path,
                mime_type,
                file_size_bytes,
                created_at_utc)
            VALUES (
                $provider,
                $prompt,
                $negativePrompt,
                $model,
                $width,
                $height,
                $steps,
                $batchSize,
                $seed,
                $fileName,
                $relativePath,
                $mimeType,
                $fileSizeBytes,
                $createdAtUtc);
            SELECT last_insert_rowid();
            """;
        AddImageParameters(command, image, relativePath);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        long id = Convert.ToInt64(value);
        return image with { Id = id };
    }

    public async Task<IReadOnlyList<GeneratedImage>> ListAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        int normalizedLimit = Math.Clamp(limit, 1, 200);
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                provider,
                prompt,
                negative_prompt,
                model,
                width,
                height,
                steps,
                batch_size,
                seed,
                file_name,
                mime_type,
                file_size_bytes,
                created_at_utc
            FROM generated_images
            ORDER BY created_at_utc DESC, id DESC
            LIMIT $limit;
            """;
        command.AddParameter("$limit", normalizedLimit);

        List<GeneratedImage> images = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            images.Add(ReadImage(reader));
        }

        return images;
    }

    public async Task<(GeneratedImage Image, string RelativePath)?> GetWithPathAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                provider,
                prompt,
                negative_prompt,
                model,
                width,
                height,
                steps,
                batch_size,
                seed,
                file_name,
                relative_path,
                mime_type,
                file_size_bytes,
                created_at_utc
            FROM generated_images
            WHERE id = $id;
            """;
        command.AddParameter("$id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        GeneratedImage image = ReadImage(reader);
        string relativePath = reader.GetString(11);
        return (image, relativePath);
    }

    public async Task<GeneratedImage?> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqliteCommand selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT
                id,
                provider,
                prompt,
                negative_prompt,
                model,
                width,
                height,
                steps,
                batch_size,
                seed,
                file_name,
                mime_type,
                file_size_bytes,
                created_at_utc
            FROM generated_images
            WHERE id = $id;
            """;
        selectCommand.AddParameter("$id", id);

        GeneratedImage? image = null;
        await using (SqliteDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                image = ReadImage(reader);
            }
        }

        if (image is null)
        {
            return null;
        }

        await using SqliteCommand deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM generated_images WHERE id = $id;";
        deleteCommand.AddParameter("$id", id);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return image;
    }

    private static void AddImageParameters(SqliteCommand command, GeneratedImage image, string relativePath)
    {
        command.AddParameter("$provider", image.Provider);
        command.AddParameter("$prompt", image.Prompt);
        command.AddParameter("$negativePrompt", image.NegativePrompt);
        command.AddParameter("$model", image.Model);
        command.AddParameter("$width", image.Width);
        command.AddParameter("$height", image.Height);
        command.AddParameter("$steps", image.Steps);
        command.AddParameter("$batchSize", image.BatchSize);
        command.AddParameter("$seed", image.Seed);
        command.AddParameter("$fileName", image.FileName);
        command.AddParameter("$relativePath", relativePath);
        command.AddParameter("$mimeType", image.MimeType);
        command.AddParameter("$fileSizeBytes", image.FileSizeBytes);
        command.AddParameter("$createdAtUtc", image.CreatedAtUtc.ToString("O"));
    }

    private static GeneratedImage ReadImage(SqliteDataReader reader)
    {
        int offset = reader.FieldCount == 15 ? 0 : 0;
        return new GeneratedImage(
            reader.GetInt64(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4),
            reader.GetInt32(offset + 5),
            reader.GetInt32(offset + 6),
            reader.GetInt32(offset + 7),
            reader.GetInt32(offset + 8),
            reader.IsDBNull(offset + 9) ? null : reader.GetInt64(offset + 9),
            reader.GetString(offset + 10),
            reader.GetString(offset + (reader.FieldCount == 15 ? 12 : 11)),
            reader.GetInt64(offset + (reader.FieldCount == 15 ? 13 : 12)),
            DateTimeOffset.Parse(reader.GetString(offset + (reader.FieldCount == 15 ? 14 : 13))));
    }
}

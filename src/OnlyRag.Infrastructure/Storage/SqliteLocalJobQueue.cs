using Microsoft.Data.Sqlite;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteLocalJobQueue : ILocalJobQueue
{
    private readonly ISqliteConnectionFactory connectionFactory;
    private readonly LocalJobQueueDescriptor descriptor;

    public SqliteLocalJobQueue(
        ISqliteConnectionFactory connectionFactory,
        LocalJobQueueDescriptor descriptor)
    {
        this.connectionFactory = connectionFactory;
        this.descriptor = descriptor;
    }

    public async Task<LocalJob> CreateAsync(
        CreateLocalJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        string payloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson;
        string id = Guid.NewGuid().ToString("N");
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO jobs (
                id,
                type,
                status,
                priority,
                progress_percent,
                current_step,
                payload_json,
                checkpoint_json,
                error,
                retry_count,
                max_retries,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $id,
                $type,
                $status,
                $priority,
                0,
                '',
                $payloadJson,
                '{}',
                NULL,
                0,
                $maxRetries,
                $now,
                $now
            );
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$type", request.Type);
        command.AddParameter("$status", JobStatus.Pending.ToString());
        command.AddParameter("$priority", request.Priority);
        command.AddParameter("$payloadJson", payloadJson);
        command.AddParameter("$maxRetries", request.MaxRetries ?? descriptor.MaxRetries);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (await GetAsync(id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<LocalJob>> ListAsync(
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        int normalizedLimit = Math.Clamp(limit ?? 200, 1, 500);
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, type, status, priority, progress_percent, current_step, payload_json,
                   checkpoint_json, error, retry_count, max_retries, created_at_utc, updated_at_utc
            FROM jobs
            ORDER BY
                CASE status
                    WHEN 'Running' THEN 0
                    WHEN 'Pausing' THEN 1
                    WHEN 'Pending' THEN 2
                    WHEN 'Paused' THEN 3
                    WHEN 'Failed' THEN 4
                    WHEN 'Completed' THEN 5
                    WHEN 'Cancelled' THEN 6
                    ELSE 7
                END,
                priority DESC,
                created_at_utc ASC
            LIMIT $limit;
            """;
        command.AddParameter("$limit", normalizedLimit);

        List<LocalJob> jobs = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    public async Task<LocalJob?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, id, cancellationToken);
    }

    public Task<LocalJob?> CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(
            id,
            JobStatus.Cancelled,
            [JobStatus.Pending, JobStatus.Running, JobStatus.Pausing, JobStatus.Paused, JobStatus.Failed],
            error: null,
            cancellationToken);
    }

    public Task<LocalJob?> PauseAsync(string id, CancellationToken cancellationToken = default)
    {
        return RequestPauseAsync(id, cancellationToken);
    }

    public Task<LocalJob?> CompletePauseAsync(string id, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(
            id,
            JobStatus.Paused,
            [JobStatus.Pausing],
            error: null,
            cancellationToken);
    }

    public Task<LocalJob?> ResumeAsync(string id, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(
            id,
            JobStatus.Pending,
            [JobStatus.Paused, JobStatus.Failed],
            error: null,
            cancellationToken);
    }

    public async Task<LocalJob?> SaveCheckpointAsync(
        string id,
        LocalJobCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        int progressPercent = Math.Clamp(checkpoint.ProgressPercent, 0, 100);
        string checkpointJson = string.IsNullOrWhiteSpace(checkpoint.CheckpointJson) ? "{}" : checkpoint.CheckpointJson;
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE jobs
            SET progress_percent = $progressPercent,
                current_step = $currentStep,
                checkpoint_json = $checkpointJson,
                updated_at_utc = $now
            WHERE id = $id
              AND status IN ('Pending', 'Running', 'Pausing', 'Paused');
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$progressPercent", progressPercent);
        command.AddParameter("$currentStep", checkpoint.CurrentStep);
        command.AddParameter("$checkpointJson", checkpointJson);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(connection, id, cancellationToken);
    }

    public async Task<int> RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE jobs
            SET status = CASE WHEN status = 'Pausing' THEN 'Paused' ELSE 'Pending' END,
                current_step = CASE
                    WHEN status = 'Pausing' THEN 'Paused'
                    WHEN current_step = '' THEN 'Ripresa dopo interruzione'
                    ELSE current_step
                END,
                updated_at_utc = $now
            WHERE status IN ('Running', 'Pausing');
            """;
        command.AddParameter("$now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LocalJob?> TryLeaseNextAsync(CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string? id;

        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                """
                SELECT id
                FROM jobs
                WHERE status = 'Pending'
                ORDER BY priority DESC, created_at_utc ASC
                LIMIT 1;
                """;
            id = await select.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (id is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE jobs
                SET status = 'Running',
                    error = NULL,
                    current_step = CASE WHEN current_step = '' THEN 'In esecuzione' ELSE current_step END,
                    updated_at_utc = $now
                WHERE id = $id
                  AND status = 'Pending';
                """;
            update.AddParameter("$id", id);
            update.AddParameter("$now", now);
            int changed = await update.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(connection, id, cancellationToken);
    }

    public Task<LocalJob?> CompleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(
            id,
            JobStatus.Completed,
            [JobStatus.Running],
            error: null,
            cancellationToken);
    }

    public async Task<LocalJob?> FailAsync(
        string id,
        string errorMessage,
        bool retryable,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        LocalJob? current = await GetAsync(connection, id, cancellationToken);
        if (current is null || current.Status is not JobStatus.Running)
        {
            return current;
        }

        int nextRetryCount = current.RetryCount + 1;
        JobStatus nextStatus = retryable && nextRetryCount <= current.MaxRetries
            ? JobStatus.Pending
            : JobStatus.Failed;
        string currentStep = nextStatus is JobStatus.Pending
            ? "Retry pianificato"
            : "Errore";

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE jobs
            SET status = $status,
                error = $error,
                retry_count = $retryCount,
                current_step = $currentStep,
                updated_at_utc = $now
            WHERE id = $id
              AND status = 'Running';
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$status", nextStatus.ToString());
        command.AddParameter("$error", errorMessage);
        command.AddParameter("$retryCount", nextRetryCount);
        command.AddParameter("$currentStep", currentStep);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(connection, id, cancellationToken);
    }

    private async Task<LocalJob?> RequestPauseAsync(
        string id,
        CancellationToken cancellationToken)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE jobs
            SET status = CASE
                    WHEN status = 'Running' THEN 'Pausing'
                    ELSE 'Paused'
                END,
                current_step = CASE
                    WHEN status = 'Running' THEN 'Pausa in corso'
                    ELSE 'Paused'
                END,
                updated_at_utc = $now
            WHERE id = $id
              AND status IN ('Pending', 'Running');
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(connection, id, cancellationToken);
    }

    private async Task<LocalJob?> TransitionAsync(
        string id,
        JobStatus targetStatus,
        IReadOnlyCollection<JobStatus> allowedCurrentStatuses,
        string? error,
        CancellationToken cancellationToken)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        string allowed = string.Join(", ", allowedCurrentStatuses.Select(status => $"'{status}'"));

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE jobs
            SET status = $status,
                error = $error,
                progress_percent = CASE WHEN $status = 'Completed' THEN 100 ELSE progress_percent END,
                current_step = $currentStep,
                updated_at_utc = $now
            WHERE id = $id
              AND status IN ({allowed});
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$status", targetStatus.ToString());
        command.AddParameter("$error", error);
        command.AddParameter("$currentStep", targetStatus.ToString());
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAsync(connection, id, cancellationToken);
    }

    private static async Task<LocalJob?> GetAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, type, status, priority, progress_percent, current_step, payload_json,
                   checkpoint_json, error, retry_count, max_retries, created_at_utc, updated_at_utc
            FROM jobs
            WHERE id = $id;
            """;
        command.AddParameter("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM jobs WHERE id = $id AND status NOT IN ('Running', 'Pausing', 'Pending');";
        command.AddParameter("$id", id);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<int> PurgeCompletedAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM jobs WHERE status IN ('Completed', 'Cancelled', 'Failed');";
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LocalJob ReadJob(SqliteDataReader reader)
    {
        return new LocalJob(
            reader.GetString(0),
            reader.GetString(1),
            Enum.Parse<JobStatus>(reader.GetString(2), ignoreCase: false),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }
}

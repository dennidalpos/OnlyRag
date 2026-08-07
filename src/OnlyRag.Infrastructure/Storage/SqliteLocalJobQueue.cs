using Microsoft.Data.Sqlite;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteLocalJobQueue : ILocalJobQueue
{
    private static readonly JobStatus[] CheckpointWritableStatuses =
        [JobStatus.Pending, JobStatus.Running, JobStatus.Pausing, JobStatus.Paused];
    private static readonly JobStatus[] InterruptedStatuses = [JobStatus.Running, JobStatus.Pausing];
    private static readonly JobStatus[] PauseRequestStatuses = [JobStatus.Pending, JobStatus.Running];
    private static readonly JobStatus[] DeleteBlockedStatuses =
        [JobStatus.Running, JobStatus.Pausing, JobStatus.Pending];
    private static readonly JobStatus[] PurgeableStatuses =
        [JobStatus.Completed, JobStatus.Cancelled, JobStatus.Failed];

    private readonly ISqliteConnectionFactory connectionFactory;
    private readonly LocalJobQueueDescriptor descriptor;
    private readonly IJobProgressNotifier? notifier;
    private readonly SemaphoreSlim _enqueueSignal = new(0);

    public SemaphoreSlim EnqueueSignal => _enqueueSignal;

    public SqliteLocalJobQueue(
        ISqliteConnectionFactory connectionFactory,
        LocalJobQueueDescriptor descriptor,
        IJobProgressNotifier? notifier = null)
    {
        this.connectionFactory = connectionFactory;
        this.descriptor = descriptor;
        this.notifier = notifier;
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
                next_attempt_at_utc,
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
                NULL,
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

        _enqueueSignal.Release();

        LocalJob created = (await GetAsync(id, cancellationToken))!;
        if (notifier is not null)
        {
            await notifier.NotifyProgressAsync(created.Id, created.Type, created.ProgressPercent, created.Status.ToString(), created.CurrentStep);
        }

        return created;
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
                   checkpoint_json, error, retry_count, max_retries, next_attempt_at_utc, created_at_utc, updated_at_utc
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
                next_attempt_at_utc ASC NULLS FIRST,
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

    public async Task<LocalJob?> SaveCheckpointAsync(
        string id,
        LocalJobCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        int progressPercent = Math.Clamp(checkpoint.ProgressPercent, 0, 100);
        string checkpointJson = string.IsNullOrWhiteSpace(checkpoint.CheckpointJson) ? "{}" : checkpoint.CheckpointJson;
        string now = DateTimeOffset.UtcNow.ToString("O");
        string writableStatusPredicate = SqliteStatusConstraints.BuildJobStatusInPredicate(CheckpointWritableStatuses);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE jobs
            SET progress_percent = $progressPercent,
                current_step = $currentStep,
                checkpoint_json = $checkpointJson,
                updated_at_utc = $now
            WHERE id = $id
              AND {writableStatusPredicate};
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$progressPercent", progressPercent);
        command.AddParameter("$currentStep", checkpoint.CurrentStep);
        command.AddParameter("$checkpointJson", checkpointJson);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        LocalJob? updated = await GetAsync(connection, id, cancellationToken);
        if (updated is not null && notifier is not null)
        {
            await notifier.NotifyProgressAsync(updated.Id, updated.Type, updated.ProgressPercent, updated.Status.ToString(), updated.CurrentStep);
        }

        return updated;
    }

    public async Task<int> RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        string interruptedStatusPredicate = SqliteStatusConstraints.BuildJobStatusInPredicate(InterruptedStatuses);
        string pausingStatusPredicate = SqliteStatusConstraints.BuildJobStatusEqualsPredicate(JobStatus.Pausing);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE jobs
            SET status = CASE WHEN {pausingStatusPredicate} THEN $pausedStatus ELSE $pendingStatus END,
                current_step = CASE
                    WHEN {pausingStatusPredicate} THEN $pausedStatus
                    WHEN current_step = '' THEN 'Ripresa dopo interruzione'
                    ELSE current_step
                END,
                next_attempt_at_utc = NULL,
                updated_at_utc = $now
            WHERE {interruptedStatusPredicate};
            """;
        command.AddParameter("$pausedStatus", JobStatus.Paused.ToString());
        command.AddParameter("$pendingStatus", JobStatus.Pending.ToString());
        command.AddParameter("$now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LocalJob?> TryLeaseNextAsync(CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        string pendingStatusPredicate = SqliteStatusConstraints.BuildJobStatusEqualsPredicate(JobStatus.Pending);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string? id;

        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                $"""
                SELECT id
                FROM jobs
                WHERE {pendingStatusPredicate}
                  AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= $now)
                ORDER BY priority DESC, created_at_utc ASC
                LIMIT 1;
                """;
            select.AddParameter("$now", now);
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
                $"""
                UPDATE jobs
                SET status = $runningStatus,
                    error = NULL,
                    next_attempt_at_utc = NULL,
                    current_step = CASE WHEN current_step = '' THEN 'In esecuzione' ELSE current_step END,
                    updated_at_utc = $now
                WHERE id = $id
                  AND {pendingStatusPredicate};
                """;
            update.AddParameter("$id", id);
            update.AddParameter("$runningStatus", JobStatus.Running.ToString());
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
        DateTimeOffset nowTimestamp = DateTimeOffset.UtcNow;
        string now = nowTimestamp.ToString("O");
        string runningStatusPredicate = SqliteStatusConstraints.BuildJobStatusEqualsPredicate(JobStatus.Running);

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
        DateTimeOffset? nextAttemptAt = nextStatus is JobStatus.Pending
            ? nowTimestamp.Add(ComputeRetryDelay(nextRetryCount))
            : null;
        string currentStep = nextStatus is JobStatus.Pending
            ? "Retry pianificato"
            : "Errore";
        string storedError = BuildRetryAwareError(errorMessage, retryable, current.MaxRetries, nextRetryCount, nextAttemptAt);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE jobs
            SET status = $status,
                error = $error,
                retry_count = $retryCount,
                next_attempt_at_utc = $nextAttemptAt,
                current_step = $currentStep,
                updated_at_utc = $now
            WHERE id = $id
              AND {runningStatusPredicate};
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$status", nextStatus.ToString());
        command.AddParameter("$error", storedError);
        command.AddParameter("$retryCount", nextRetryCount);
        command.AddParameter("$nextAttemptAt", nextAttemptAt?.ToString("O"));
        command.AddParameter("$currentStep", currentStep);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        LocalJob? updated = await GetAsync(connection, id, cancellationToken);
        if (updated is not null && notifier is not null)
        {
            if (updated.Status == JobStatus.Failed)
            {
                await notifier.NotifyFailedAsync(updated.Id, updated.Type, updated.Error ?? errorMessage);
            }
            else
            {
                await notifier.NotifyProgressAsync(updated.Id, updated.Type, updated.ProgressPercent, updated.Status.ToString(), updated.CurrentStep);
            }
        }

        return updated;
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

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        string deleteStatusPredicate = SqliteStatusConstraints.BuildJobStatusNotInPredicate(DeleteBlockedStatuses);
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM jobs WHERE id = $id AND {deleteStatusPredicate};";
        command.AddParameter("$id", id);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<int> PurgeCompletedAsync(CancellationToken cancellationToken = default)
    {
        string purgeStatusPredicate = SqliteStatusConstraints.BuildJobStatusInPredicate(PurgeableStatuses);
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM jobs WHERE {purgeStatusPredicate};";
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<LocalJob?> RequestPauseAsync(
        string id,
        CancellationToken cancellationToken)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        string pauseRequestStatusPredicate = SqliteStatusConstraints.BuildJobStatusInPredicate(PauseRequestStatuses);
        string runningStatusPredicate = SqliteStatusConstraints.BuildJobStatusEqualsPredicate(JobStatus.Running);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE jobs
            SET status = CASE
                    WHEN {runningStatusPredicate} THEN $pausingStatus
                    ELSE $pausedStatus
                END,
                current_step = CASE
                    WHEN {runningStatusPredicate} THEN 'Pausa in corso'
                    ELSE $pausedStatus
                END,
                next_attempt_at_utc = NULL,
                updated_at_utc = $now
            WHERE id = $id
              AND {pauseRequestStatusPredicate};
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$pausingStatus", JobStatus.Pausing.ToString());
        command.AddParameter("$pausedStatus", JobStatus.Paused.ToString());
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
        string allowedStatusPredicate = SqliteStatusConstraints.BuildJobStatusInPredicate(allowedCurrentStatuses);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE jobs
            SET status = $status,
                error = $error,
                next_attempt_at_utc = NULL,
                progress_percent = CASE WHEN $status = $completedStatus THEN 100 ELSE progress_percent END,
                current_step = $currentStep,
                updated_at_utc = $now
            WHERE id = $id
              AND {allowedStatusPredicate};
            """;
        command.AddParameter("$id", id);
        command.AddParameter("$status", targetStatus.ToString());
        command.AddParameter("$error", error);
        command.AddParameter("$completedStatus", JobStatus.Completed.ToString());
        command.AddParameter("$currentStep", targetStatus.ToString());
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        LocalJob? updated = await GetAsync(connection, id, cancellationToken);
        if (updated is not null && notifier is not null)
        {
            if (targetStatus == JobStatus.Completed)
            {
                await notifier.NotifyCompletedAsync(updated.Id, updated.Type);
            }
            else
            {
                await notifier.NotifyProgressAsync(updated.Id, updated.Type, updated.ProgressPercent, updated.Status.ToString(), updated.CurrentStep);
            }
        }

        return updated;
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
                   checkpoint_json, error, retry_count, max_retries, next_attempt_at_utc, created_at_utc, updated_at_utc
            FROM jobs
            WHERE id = $id;
            """;
        command.AddParameter("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
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
            reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)),
            DateTimeOffset.Parse(reader.GetString(13)));
    }

    private static TimeSpan ComputeRetryDelay(int retryCount)
    {
        int seconds = retryCount switch
        {
            <= 1 => 5,
            2 => 15,
            3 => 45,
            _ => 120
        };

        return TimeSpan.FromSeconds(seconds);
    }

    private static string BuildRetryAwareError(
        string errorMessage,
        bool retryable,
        int maxRetries,
        int nextRetryCount,
        DateTimeOffset? nextAttemptAt)
    {
        if (!retryable)
        {
            return $"{errorMessage} Retry non previsto: errore permanente o validazione non recuperabile.";
        }

        int retriesRemaining = Math.Max(0, maxRetries - nextRetryCount);
        if (nextAttemptAt is null)
        {
            return $"{errorMessage} Retry esauriti: 0 tentativi rimanenti. Se coinvolge Ollama, verifica servizio, modello, num_ctx e ollama ps.";
        }

        return $"{errorMessage} Retry automatico pianificato alle {nextAttemptAt:O}; tentativi rimanenti: {retriesRemaining}. Se coinvolge Ollama, verifica servizio, modello, num_ctx e ollama ps.";
    }
}

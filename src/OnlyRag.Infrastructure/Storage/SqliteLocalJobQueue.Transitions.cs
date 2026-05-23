using Microsoft.Data.Sqlite;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteLocalJobQueue
{
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

        return await GetAsync(connection, id, cancellationToken);
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
}

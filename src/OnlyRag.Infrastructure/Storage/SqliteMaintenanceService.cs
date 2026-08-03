using System.Diagnostics;
using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteMaintenanceService : ISqliteMaintenanceService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly LocalSqliteStoreDescriptor _descriptor;
    private DateTimeOffset? _lastMaintenanceAtUtc;
    private string _lastMaintenanceStatus = "No maintenance executed yet.";

    public SqliteMaintenanceService(
        ISqliteConnectionFactory connectionFactory,
        LocalSqliteStoreDescriptor descriptor)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public async Task<SqliteDatabaseStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string dbPath = _descriptor.Paths.DatabasePath;
        bool exists = File.Exists(dbPath);
        long fileSize = exists ? new FileInfo(dbPath).Length : 0;
        bool fts5Available = false;

        if (exists)
        {
            try
            {
                await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

                // Check if FTS5 is compiled into SQLite
                await using SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA compile_options;";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    string option = reader.GetString(0);
                    if (option.Contains("ENABLE_FTS5", StringComparison.OrdinalIgnoreCase))
                    {
                        fts5Available = true;
                        break;
                    }
                }

                if (!fts5Available)
                {
                    // Fallback check table existence
                    await using SqliteCommand tableCmd = connection.CreateCommand();
                    tableCmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='document_chunks_fts';";
                    var result = await tableCmd.ExecuteScalarAsync(cancellationToken);
                    fts5Available = result is long count && count > 0;
                }
            }
            catch
            {
                fts5Available = false;
            }
        }

        return new SqliteDatabaseStatusResponse(
            DatabasePath: dbPath,
            Exists: exists,
            FileSizeBytes: fileSize,
            FormattedFileSize: FormatBytes(fileSize),
            Fts5Enabled: fts5Available,
            LastMaintenanceAtUtc: _lastMaintenanceAtUtc,
            MaintenanceStatus: _lastMaintenanceStatus);
    }

    public async Task<SqliteMaintenanceResultResponse> RunMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        string dbPath = _descriptor.Paths.DatabasePath;
        if (!File.Exists(dbPath))
        {
            return new SqliteMaintenanceResultResponse(
                Success: false,
                InitialFileSizeBytes: 0,
                FinalFileSizeBytes: 0,
                BytesReclaimed: 0,
                Duration: TimeSpan.Zero,
                Message: "Database file does not exist.",
                ExecutedAtUtc: DateTimeOffset.UtcNow);
        }

        Stopwatch sw = Stopwatch.StartNew();
        long initialSize = new FileInfo(dbPath).Length;

        try
        {
            await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

            // 1. Optimize SQLite internal query planner
            await using (SqliteCommand optCmd = connection.CreateCommand())
            {
                optCmd.CommandText = "PRAGMA optimize;";
                await optCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // 2. Re-index and compact FTS5 table if it exists
            await using (SqliteCommand ftsCmd = connection.CreateCommand())
            {
                ftsCmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='document_chunks_fts';";
                var ftsExists = await ftsCmd.ExecuteScalarAsync(cancellationToken);
                if (ftsExists is long count && count > 0)
                {
                    await using SqliteCommand ftsOptimize = connection.CreateCommand();
                    ftsOptimize.CommandText = "INSERT INTO document_chunks_fts(document_chunks_fts) VALUES('optimize');";
                    await ftsOptimize.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // 3. Run Incremental Vacuum and full Vacuum
            await using (SqliteCommand incVacCmd = connection.CreateCommand())
            {
                incVacCmd.CommandText = "PRAGMA incremental_vacuum;";
                await incVacCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand vacCmd = connection.CreateCommand())
            {
                vacCmd.CommandText = "VACUUM;";
                await vacCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            sw.Stop();
            long finalSize = new FileInfo(dbPath).Length;
            long reclaimed = Math.Max(0, initialSize - finalSize);
            _lastMaintenanceAtUtc = DateTimeOffset.UtcNow;
            _lastMaintenanceStatus = $"Completed in {sw.ElapsedMilliseconds}ms. Reclaimed {FormatBytes(reclaimed)}.";

            return new SqliteMaintenanceResultResponse(
                Success: true,
                InitialFileSizeBytes: initialSize,
                FinalFileSizeBytes: finalSize,
                BytesReclaimed: reclaimed,
                Duration: sw.Elapsed,
                Message: _lastMaintenanceStatus,
                ExecutedAtUtc: _lastMaintenanceAtUtc.Value);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _lastMaintenanceStatus = $"Failed: {ex.Message}";
            return new SqliteMaintenanceResultResponse(
                Success: false,
                InitialFileSizeBytes: initialSize,
                FinalFileSizeBytes: initialSize,
                BytesReclaimed: 0,
                Duration: sw.Elapsed,
                Message: $"Maintenance failed: {ex.Message}",
                ExecutedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
}

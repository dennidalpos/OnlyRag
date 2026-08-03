using System.Collections.Concurrent;
using System.IO;

namespace OnlyRag.Infrastructure.Agent;

public record WorkspaceSnapshotCheckpoint(
    string CheckpointId,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string?> FileSnapshots
);

/// <summary>
/// Manages transient workspace snapshot checkpoints and rollbacks for Tree-of-Thought (ToT)
/// MCTS state machine execution.
/// </summary>
public sealed class WorkspaceSnapshotCheckpointManager
{
    private readonly ConcurrentDictionary<string, WorkspaceSnapshotCheckpoint> activeCheckpoints = new();
    private readonly ConcurrentDictionary<string, string?> fileBaselineCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Captures a workspace snapshot checkpoint for the specified file paths.
    /// </summary>
    public WorkspaceSnapshotCheckpoint CreateCheckpoint(string checkpointId, string workspaceRoot, IEnumerable<string> targetPaths)
    {
        var snapshots = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in targetPaths)
        {
            string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(workspaceRoot, path);
            if (File.Exists(fullPath))
            {
                snapshots[fullPath] = File.ReadAllText(fullPath);
            }
            else
            {
                snapshots[fullPath] = null; // File did not exist prior to checkpoint
            }
        }

        var checkpoint = new WorkspaceSnapshotCheckpoint(checkpointId, DateTimeOffset.UtcNow, snapshots);
        activeCheckpoints[checkpointId] = checkpoint;
        return checkpoint;
    }

    /// <summary>
    /// Restores the workspace to the exact state saved in the checkpoint (rolling back changes).
    /// </summary>
    public bool RestoreCheckpoint(WorkspaceSnapshotCheckpoint checkpoint)
    {
        try
        {
            foreach (var (fullPath, originalContent) in checkpoint.FileSnapshots)
            {
                if (originalContent is null)
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                }
                else
                {
                    string? dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(fullPath, originalContent);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes a completed checkpoint.
    /// </summary>
    public bool ReleaseCheckpoint(string checkpointId)
    {
        return activeCheckpoints.TryRemove(checkpointId, out _);
    }
}

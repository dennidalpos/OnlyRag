namespace OnlyRag.Core;

public sealed record UpdateManifest(
    string Version,
    IReadOnlyList<UpdateFileEntry> Files);

public sealed record UpdateFileEntry(
    string Path,
    string Sha256,
    long SizeBytes);

public sealed record UpdateResult(
    string Version,
    IReadOnlyList<string> UpdatedFiles,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<UpdateFailure> FailedFiles,
    ModelIntegrityStatus ModelIntegrity);

public sealed record UpdateFailure(string Path, string Error);

public sealed record ModelIntegrityStatus(
    bool IsHealthy,
    IReadOnlyList<ModelIntegrityIssue> Issues,
    bool RequiresOnDemandRepair)
{
    public static ModelIntegrityStatus Healthy() => new(true, [], false);
}

public sealed record ModelIntegrityIssue(
    string Path,
    string Reason,
    string DiagnosticAction = "download");

namespace OnlyRag.Core;

public sealed record WorkspaceConfig(
    string? RootPath,
    bool IsAuthorized,
    bool CanRead,
    bool CanWrite,
    int FileCount,
    DateTimeOffset? LastVerifiedAt);

public sealed record SelectWorkspaceRequest(
    string FolderPath);

public sealed record WorkspaceFileItem(
    string RelativePath,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset LastModified);

public sealed record ReadWorkspaceFileRequest(
    string RelativePath);

public sealed record ReadWorkspaceFileResponse(
    string RelativePath,
    string Content,
    long SizeBytes,
    string Language);

public sealed record WriteWorkspaceFileRequest(
    string RelativePath,
    string Content);

public sealed record WriteWorkspaceFileResponse(
    string RelativePath,
    bool Success,
    string Message);

public sealed record OpenExternalFileRequest(
    string Path);

public sealed record DeleteWorkspaceFileRequest(
    string RelativePath);

public sealed record DeleteWorkspaceFileResponse(
    string RelativePath,
    bool Success,
    string Message);

public sealed record ExecuteWorkspaceCommandRequest(
    string Command,
    string? Arguments = null);

public sealed record ExecuteWorkspaceCommandResponse(
    bool Success,
    int ExitCode,
    string Output,
    string Error);



namespace OnlyRag.Core;

public sealed record StorageStatusResponse(
    string Provider,
    string DatabasePath,
    bool DatabaseExists,
    int CurrentSchemaVersion,
    int TargetSchemaVersion,
    string SchemaStatus,
    bool Fts5Available,
    string? TechnicalNote);
